using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace NDSTK.Booking.Services;

/// <summary>
/// Reads the training classes out of the content tree and projects them for one member.
/// </summary>
/// <remarks>
/// This is the only place that knows a class is a content node. Everything above it works with the
/// <see cref="TrainingClass"/> and <see cref="BookableClass"/> records, so the rules never touch
/// <see cref="IPublishedContent"/>.
/// </remarks>
public sealed class TrainingClassService(
    IPublishedContentQuery contentQuery,
    IBookingRepository bookings)
{
    private const string ClassAlias = "trainingClass";
    private const int DefaultDurationMinutes = 60;

    /// <summary>
    /// Upcoming classes, soonest first, each projected for the account's children. Pass an empty
    /// collection for an anonymous visitor.
    /// </summary>
    /// <remarks>
    /// Takes the children rather than the account because a class can be bookable for one child and
    /// not another - which is the normal case on a family account where one sibling is already on it.
    /// </remarks>
    public async Task<IReadOnlyList<BookableClass>> GetUpcomingAsync(
        IReadOnlyCollection<Guid> participantKeys, DateTime nowUtc)
    {
        TrainingClass[] classes = ReadClasses()
            .Where(trainingClass => trainingClass.StartUtc > nowUtc)
            .OrderBy(trainingClass => trainingClass.StartUtc)
            .ToArray();

        if (classes.Length == 0)
        {
            return [];
        }

        IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>> byClass =
            await bookings.GetBookingsByClassAsync([.. classes.Select(trainingClass => trainingClass.Key)]);

        return
        [
            .. classes.Select(trainingClass => BookableClass.From(
                trainingClass,
                byClass.TryGetValue(trainingClass.Key, out IReadOnlyList<BookingSnapshot>? forClass)
                    ? forClass
                    : [],
                participantKeys,
                nowUtc)),
        ];
    }

    /// <summary>Looks one class up by key, for the booking flow to validate against.</summary>
    public TrainingClass? Find(Guid classKey)
        => ReadClasses().FirstOrDefault(trainingClass => trainingClass.Key == classKey);

    private IEnumerable<TrainingClass> ReadClasses()
        => contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(ClassAlias))
            .Select(ToTrainingClass);

    private static TrainingClass ToTrainingClass(IPublishedContent content)
    {
        // The editor picks Swedish local time; everything stored and compared is UTC.
        DateTime start = SwedishTime.ToUtc(content.Value<DateTime>("start"));

        var duration = content.Value<int>("durationMinutes");

        return new TrainingClass(
            Key: content.Key,
            Title: content.Value<string>("title").IfNullOrWhiteSpace(content.Name),
            Description: content.Value<string>("description"),
            StartUtc: start,
            DurationMinutes: duration > 0 ? duration : DefaultDurationMinutes,
            // Capacity is read as-is: a missing value is zero, and BookableClass treats zero as
            // "not bookable" rather than "unlimited".
            Capacity: content.Value<int>("capacity"),
            Instructor: content.Value<string>("instructor"),
            Location: content.Value<string>("location"));
    }
}
