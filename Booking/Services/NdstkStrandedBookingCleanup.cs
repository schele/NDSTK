using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NPoco;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Services;

/// <summary>
/// Releases places still held by children who have been removed from their account.
/// </summary>
/// <remarks>
/// Removing a child used to be a soft delete and nothing else, which left their future bookings
/// standing: the seat stayed reserved against the class's capacity and the child kept appearing on
/// the coach's roster, while the parent believed they were gone.
///
/// <see cref="Web.ParticipantSurfaceController.Remove"/> handles it now, but only from here on.
/// This clears up what the old behaviour left behind, on any database that has some - which is why
/// it is a guarded one-off rather than a hand-written UPDATE against one environment.
///
/// Each stranded booking is cancelled and credited exactly as if the member had pressed "Avboka",
/// because that is what they thought they were doing. Past bookings are left alone: cancelling
/// those would rewrite attendance that already happened.
/// </remarks>
internal sealed class NdstkStrandedBookingCleanup(
    IScopeProvider scopeProvider,
    IBookingRepository bookings,
    IKeyValueService keyValueService,
    ILogger<NdstkStrandedBookingCleanup> logger)
{
    private const string StateKey = "NDSTK/StrandedBookingCleanup";
    private const string StateValue = "removed-participants-v1";

    public async Task RunAsync()
    {
        if (keyValueService.GetValue(StateKey) == StateValue)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        IReadOnlyList<Stranded> stranded = await FindStrandedAsync(nowUtc);

        var cancelled = 0;
        var credited = 0;

        foreach (Stranded row in stranded)
        {
            (var justCancelled, var justCredited) =
                await bookings.CancelFutureBookingsForParticipantAsync(
                    row.ParticipantKey, row.MemberKey, nowUtc);

            cancelled += justCancelled;
            credited += justCredited;
        }

        keyValueService.SetValue(StateKey, StateValue);

        if (cancelled == 0)
        {
            logger.LogDebug("No stranded bookings to release.");
            return;
        }

        logger.LogInformation(
            "Released {Cancelled} booking(s) still held by removed children, issuing {Credited} credit(s).",
            cancelled, credited);
    }

    /// <summary>
    /// One row per removed child that still holds a live place on a class that has not run yet.
    /// </summary>
    private async Task<IReadOnlyList<Stranded>> FindStrandedAsync(DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        return await scope.Database.FetchAsync<Stranded>(
            $"""
            SELECT DISTINCT p.Key AS ParticipantKey, b.MemberKey AS MemberKey
            FROM {BookingTables.Booking} b
            JOIN {BookingTables.Participant} p ON p.Key = b.ParticipantKey
            WHERE p.RemovedUtc IS NOT NULL
              AND b.Status IN (@0, @1)
              AND b.ClassStartUtc > @2
            """,
            Domain.BookingStatus.Confirmed, Domain.BookingStatus.Pending, nowUtc);
    }

    private sealed class Stranded
    {
        public Guid ParticipantKey { get; set; }
        public Guid MemberKey { get; set; }
    }
}
