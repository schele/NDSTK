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
    private const string SettingsAlias = "settings";
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
    {
        IPublishedContent[] roots = [.. contentQuery.ContentAtRoot()];

        // Read once for the whole set rather than per class: it is the same address on every one of
        // them, and turning it into a URL is the only work either way.
        var mapUrl = MapLink.ForAddress(ReadVenueAddress(roots));

        return roots
            .SelectMany(root => root.DescendantsOrSelfOfType(ClassAlias))
            .Select(content => ToTrainingClass(content, mapUrl));
    }

    /// <summary>The club's address, from the Settings node under the site root.</summary>
    /// <remarks>
    /// One address for the club, not one per class: the court an editor types on a class ("Bana 2")
    /// is not something a map can find. A site with no address configured simply has no link.
    /// </remarks>
    private static string? ReadVenueAddress(IEnumerable<IPublishedContent> roots)
        => roots
            .Select(root => root.ChildrenOfType(SettingsAlias).FirstOrDefault())
            .OfType<IPublishedContent>()
            .Select(settings => settings.Value<string>("venueAddress"))
            .FirstOrDefault(address => string.IsNullOrWhiteSpace(address) is false);

    private static TrainingClass ToTrainingClass(IPublishedContent content, string? mapUrl)
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
            Instructor: ReadInstructor(content),
            Location: content.Value<string>("location"),
            MapUrl: mapUrl);
    }

    /// <summary>
    /// The picked coach, or nothing. Falls back to the retired text field so a class an editor has
    /// not repicked yet still shows a name - the backfill fills the picker in, but an editor who
    /// clears it should not silently blank the listing.
    /// </summary>
    /// <remarks>
    /// This is the only place that knows an instructor is a content node, in the same way this class
    /// is the only place that knows a training class is one. The media item is resolved to a URL
    /// here too, so nothing above ever holds an IPublishedContent.
    /// </remarks>
    private static ClassInstructor? ReadInstructor(IPublishedContent content)
    {
        IPublishedContent? coach = content.Value<IPublishedContent>("coach");

        if (coach is null)
        {
            var legacy = content.Value<string>("instructor");
            return string.IsNullOrWhiteSpace(legacy) ? null : new ClassInstructor(legacy);
        }

        return new ClassInstructor(
            Name: coach.Value<string>("name").IfNullOrWhiteSpace(coach.Name),
            Title: coach.Value<string>("role"),
            Quote: coach.Value<string>("quote"),
            // Rich text comes back as IHtmlEncodedString; ToString gives the markup the view writes
            // out raw. Only backoffice users author it.
            Merits: coach.Value<object>("merits")?.ToString(),
            PhotoUrl: coach.Value<IPublishedContent>("photo")?.Url());
    }
}
