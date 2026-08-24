using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace NDSTK.Booking.Notifications;

/// <summary>
/// Keeps bookings honest when an editor changes a class in the backoffice.
/// </summary>
/// <remarks>
/// Bookings carry their own copy of the class start time, which is what makes the reminder query one
/// indexed range scan and lets a booking still render after its class is deleted. The cost of that
/// denormalisation is exactly this handler: without it, moving a class in the backoffice would leave
/// every existing booking reminding at the old hour, silently.
/// </remarks>
internal sealed class TrainingClassChangedHandler(
    IBookingRepository repository,
    ILogger<TrainingClassChangedHandler> logger)
    : INotificationAsyncHandler<ContentPublishedNotification>,
      INotificationAsyncHandler<ContentUnpublishedNotification>,
      INotificationAsyncHandler<ContentDeletedNotification>
{
    private const string ClassAlias = "trainingClass";

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        foreach (IContent content in notification.PublishedEntities.Where(IsTrainingClass))
        {
            DateTime startUtc = SwedishTime.ToUtc(content.GetValue<DateTime>("start"));

            var affected = await repository.ResyncClassStartAsync(
                content.Key, startUtc, DateTime.UtcNow);

            if (affected > 0)
            {
                logger.LogInformation(
                    "'{Name}' moved; repointed {Count} booking(s) at the new start time.",
                    content.Name, affected);
            }
        }
    }

    public Task HandleAsync(ContentUnpublishedNotification notification, CancellationToken cancellationToken)
        => WithdrawAsync(notification.UnpublishedEntities, "unpublished");

    public Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
        => WithdrawAsync(notification.DeletedEntities, "deleted");

    /// <summary>
    /// An editor withdrawing a class cancels its bookings and issues a credit for each paid one.
    /// Nobody should lose money because the club changed its mind.
    /// </summary>
    private async Task WithdrawAsync(IEnumerable<IContent> entities, string what)
    {
        foreach (IContent content in entities.Where(IsTrainingClass))
        {
            var credited = await repository.CancelAllForClassAsync(content.Key, DateTime.UtcNow);

            if (credited > 0)
            {
                logger.LogWarning(
                    "'{Name}' was {What} with {Count} paid booking(s); each member was issued a credit.",
                    content.Name, what, credited);
            }
        }
    }

    private static bool IsTrainingClass(IContent content)
        => content.ContentType.Alias == ClassAlias;
}
