using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Resolves the provider at startup so the factory's "Payment provider: …" line is on the first
/// page of the log, not buried after the first booking of the day.
/// </summary>
public sealed class PaymentProviderAnnouncer(IPaymentProvider provider)
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        // Resolving the constructor parameter did the work. Touching Name keeps the analyser quiet
        // about an unused parameter without adding a second log line.
        _ = provider.Name;
    }
}
