using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NDSTK.Booking.Admin;
using NDSTK.Booking.Data;
using NDSTK.Booking.Data.Migrations;
using NDSTK.Booking.Jobs;
using NDSTK.Booking.Notifications;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Payments.Swish;
using NDSTK.Booking.Security;
using NDSTK.Booking.Services;
using NDSTK.Booking.Web;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace NDSTK.Booking;

/// <summary>
/// Wires up the booking feature. Grows one registration at a time as the later phases add
/// repositories, the payment provider, the reminder job and the editor-change handlers.
/// </summary>
public sealed class BookingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, BookingMigrationRunner>();

        AddMemberVerificationTokenProvider(builder);

        // Scoped, not singleton: both read per-request Umbraco state.
        builder.Services.AddScoped<MembershipSettingsService>();
        builder.Services.AddScoped<MemberProfileService>();
        builder.Services.AddScoped<BookingMailService>();
        builder.Services.AddScoped<IdentityErrorMessages>();
        builder.Services.AddScoped<MemberBookingsProvider>();
        builder.Services.AddScoped<TrainingClassService>();
        builder.Services.AddScoped<IBookingRepository, BookingRepository>();
        builder.Services.AddScoped<IParticipantRepository, ParticipantRepository>();
        builder.Services.AddScoped<BookingService>();
        builder.Services.AddScoped<NdstkParticipantBackfill>();
        builder.Services.AddScoped<NdstkStrandedBookingCleanup>();

        // Read-only reporting for the backoffice. Deliberately not on IBookingRepository.
        builder.Services.AddScoped<MemberAdminQueries>();

        // Registered unconditionally, and switched off in the gate rather than here: a route that
        // exists but answers 404 is easier to reason about than one whose absence depends on how the
        // process was started. The gate is what makes it unreachable outside development.
        builder.Services.AddScoped<TestDataReset>();
        builder.Services.AddSingleton<TestDataResetGate>();

        // Swish when enabled and the certificate loads, the mock otherwise. The factory logs which,
        // and the announcer makes it log at startup.
        builder.Services.AddOptions<SwishOptions>().Bind(builder.Config.GetSection(SwishOptions.SectionName));
        builder.AddSwishHttpClients();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IPaymentProvider>(PaymentProviderFactory.Create);
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, PaymentProviderAnnouncer>();

        builder.Services.AddSingleton<SwishCallbackUrl>();
        builder.Services.AddSingleton<SwishQrService>();

        // Recurring: sends class reminders and releases abandoned payment holds.
        builder.Services.AddRecurringBackgroundJob<ClassReminderJob>();

        // Keeps bookings in step when an editor moves, unpublishes or deletes a class.
        builder.AddNotificationAsyncHandler<ContentPublishedNotification, TrainingClassChangedHandler>();
        builder.AddNotificationAsyncHandler<ContentUnpublishedNotification, TrainingClassChangedHandler>();
        builder.AddNotificationAsyncHandler<ContentDeletedNotification, TrainingClassChangedHandler>();
    }

    /// <summary>
    /// Points member email confirmation at a token provider with its own, much shorter lifespan.
    /// </summary>
    /// <remarks>
    /// Scoped to members by which options type it configures, which is worth being explicit about.
    /// <c>MemberManager</c> takes <c>IOptions&lt;IdentityOptions&gt;</c>, while
    /// <c>BackOfficeUserManager</c> takes <c>IOptions&lt;BackOfficeIdentityOptions&gt;</c> - a
    /// derived type, and therefore a separate options instance that this delegate never runs
    /// against. So the backoffice keeps Identity's one-day default for its invite and password
    /// reset links.
    ///
    /// Both halves of the round trip read <c>Tokens.EmailConfirmationTokenProvider</c> -
    /// <c>GenerateEmailConfirmationTokenAsync</c> to issue and <c>ConfirmEmailAsync</c> to verify -
    /// so the two cannot drift apart.
    /// </remarks>
    private static void AddMemberVerificationTokenProvider(IUmbracoBuilder builder)
    {
        // Transient to match how Identity registers its own providers.
        builder.Services.AddTransient<MemberVerificationTokenProvider>();

        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.ProviderMap[MemberVerificationTokenOptions.ProviderName] =
                new TokenProviderDescriptor(typeof(MemberVerificationTokenProvider));

            options.Tokens.EmailConfirmationTokenProvider = MemberVerificationTokenOptions.ProviderName;
        });
    }
}
