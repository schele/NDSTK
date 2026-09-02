using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace NDSTK.Booking.Jobs;

/// <summary>
/// Sends class reminders and releases abandoned payment holds.
/// </summary>
/// <remarks>
/// Runs every fifteen minutes rather than hourly so the reminder lands close to the configured
/// number of hours before the class, and so an abandoned hold frees its place promptly.
///
/// Guarded by <see cref="IServerRoleAccessor"/>: on a multi-server deployment only the scheduling
/// publisher runs it. Without that check every server would send every member the same reminder.
///
/// Resolves its own dependencies from a fresh scope per run. The job itself is a singleton, so
/// injecting the scoped services directly would capture them for the process lifetime.
/// </remarks>
public sealed class ClassReminderJob(
    IServiceScopeFactory scopeFactory,
    IServerRoleAccessor serverRoleAccessor,
    ILogger<ClassReminderJob> logger)
    : IRecurringBackgroundJob
{
    public TimeSpan Period => TimeSpan.FromMinutes(15);

    /// <summary>Long enough that a restart loop cannot turn into a mail loop.</summary>
    public TimeSpan Delay => TimeSpan.FromMinutes(2);

    public event EventHandler? PeriodChanged;

    public async Task RunJobAsync()
    {
        // Silences the unused-event warning while keeping the interface contract. The period is
        // fixed, so nothing ever raises this.
        PeriodChanged?.Invoke(this, EventArgs.Empty);

        ServerRole role = serverRoleAccessor.CurrentServerRole;
        if (role is not (ServerRole.Single or ServerRole.SchedulingPublisher))
        {
            // Logged rather than silent. A job that quietly does nothing on every run is very hard
            // to tell apart from a job that runs and finds nothing to do.
            logger.LogInformation(
                "Skipping the reminder run; this server's role is {Role}.", role);
            return;
        }

        logger.LogDebug("Reminder run starting (role {Role}).", role);

        using IServiceScope scope = scopeFactory.CreateScope();
        IBookingRepository repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<MembershipSettingsService>();
        var mail = scope.ServiceProvider.GetRequiredService<BookingMailService>();
        var classes = scope.ServiceProvider.GetRequiredService<TrainingClassService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        DateTime nowUtc = DateTime.UtcNow;

        await ReconcilePaymentsAsync(repository, bookings, nowUtc);
        await SweepExpiredHoldsAsync(repository, nowUtc);
        await SendRemindersAsync(repository, mail, classes, settings, nowUtc);
    }

    /// <summary>
    /// Asks Swish about every pending payment that has a request and is older than a minute. This
    /// is what catches a lost callback for a member who closed the tab after paying.
    /// </summary>
    /// <remarks>
    /// Before the sweep, deliberately. Sweeping first would expire the booking of a payment that
    /// turns out to be PAID a moment later; the late-payment rule would still recover it, but only
    /// by re-checking capacity or issuing a credit, when simply confirming was available.
    /// </remarks>
    private async Task ReconcilePaymentsAsync(
        IBookingRepository repository, BookingService bookings, DateTime nowUtc)
    {
        IReadOnlyList<PaymentRecord> awaiting =
            await repository.GetPaymentsAwaitingReconciliationAsync(nowUtc.AddMinutes(-1));

        if (awaiting.Count == 0)
        {
            return;
        }

        var settled = 0;

        foreach (PaymentRecord payment in awaiting)
        {
            try
            {
                PaymentRecord after = await bookings.ReconcileAsync(payment, nowUtc);
                if (after.Status != PaymentStatus.Pending)
                {
                    settled++;
                }
            }
            catch (PaymentProviderException exception)
            {
                // One unreachable call must not stop the rest, or the sweep and the reminders.
                logger.LogWarning(exception, "Reconciling payment {Reference} failed; next run.", payment.Reference);
            }
        }

        logger.LogInformation(
            "Reconciled {Count} pending Swish payment(s); {Settled} reached a final state.",
            awaiting.Count, settled);
    }

    /// <summary>
    /// Releases places held by payments nobody completed. Runs before the reminders so a swept place
    /// is already free in the same pass.
    /// </summary>
    private async Task SweepExpiredHoldsAsync(IBookingRepository repository, DateTime nowUtc)
    {
        IReadOnlyList<BookingRecord> expired = await repository.GetExpiredHoldsAsync(nowUtc);
        if (expired.Count == 0)
        {
            return;
        }

        foreach (BookingRecord booking in expired)
        {
            await repository.ExpireBookingAsync(booking.Id, nowUtc);
        }

        logger.LogInformation("Released {Count} abandoned payment hold(s).", expired.Count);
    }

    private async Task SendRemindersAsync(
        IBookingRepository repository,
        BookingMailService mail,
        TrainingClassService classes,
        MembershipSettingsService settings,
        DateTime nowUtc)
    {
        var hoursBefore = settings.Get().ReminderHoursBefore;
        DateTime windowEnd = nowUtc.AddHours(hoursBefore);

        IReadOnlyList<BookingRecord> due =
            await repository.GetBookingsDueRemindersAsync(nowUtc, windowEnd);

        logger.LogDebug(
            "{Count} booking(s) due a reminder between {From:u} and {To:u}.",
            due.Count, nowUtc, windowEnd);

        if (due.Count == 0)
        {
            return;
        }

        var sent = 0;

        foreach (BookingRecord booking in due)
        {
            // Claim the booking first. A run that overlaps with another - or a restart mid-run -
            // therefore cannot send the same reminder twice; the loser simply skips it.
            if (await repository.TryStampReminderSentAsync(booking.Id, nowUtc) is false)
            {
                continue;
            }

            TrainingClass? trainingClass = classes.Find(booking.ClassKey);

            await mail.SendClassReminderAsync(
                booking.MemberKey,
                trainingClass?.Title ?? "Din träning",
                booking.ClassStartUtc,
                trainingClass?.Location,
                trainingClass?.MapUrl);

            sent++;
        }

        if (sent > 0)
        {
            logger.LogInformation(
                "Sent {Sent} class reminder(s) for classes starting within {Hours} hours.",
                sent, hoursBefore);
        }
    }
}
