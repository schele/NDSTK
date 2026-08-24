using Microsoft.Extensions.DependencyInjection;
using NDSTK.Booking.Data;
using NDSTK.Booking.Data.Migrations;
using NDSTK.Booking.Services;
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

        // Scoped, not singleton: both read per-request Umbraco state.
        builder.Services.AddScoped<MembershipSettingsService>();
        builder.Services.AddScoped<MemberProfileService>();
        builder.Services.AddScoped<BookingMailService>();
        builder.Services.AddScoped<TrainingClassService>();
        builder.Services.AddScoped<IBookingRepository, BookingRepository>();
    }
}
