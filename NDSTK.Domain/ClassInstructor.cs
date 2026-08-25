namespace NDSTK.Booking.Domain;

/// <summary>
/// The coach taking a class, as much of them as the site shows.
/// </summary>
/// <remarks>
/// A record rather than a string because a name on a class listing is now something a member can
/// click. Everything past <paramref name="Name"/> is optional: an instructor created by the backfill
/// starts as a name and nothing else, and the listing has to read correctly the whole time an editor
/// is filling the rest in.
///
/// No Umbraco types here, like the rest of this project - <see cref="PhotoUrl"/> is resolved by the
/// service that reads the content, so the rules and the views never touch a media item.
/// </remarks>
/// <param name="Merits">
/// Rich text, so an editor can write a list. Rendered as HTML, which is safe because the only
/// authors are backoffice users.
/// </param>
public sealed record ClassInstructor(
    string Name,
    string? Title = null,
    string? Quote = null,
    string? Merits = null,
    string? PhotoUrl = null)
{
    /// <summary>
    /// Whether there is anything worth opening a dialog for. A backfilled instructor with only a
    /// name should render as plain text, not as a button that opens an empty box.
    /// </summary>
    public bool HasDetails =>
        string.IsNullOrWhiteSpace(Quote) is false
        || string.IsNullOrWhiteSpace(Merits) is false
        || string.IsNullOrWhiteSpace(PhotoUrl) is false
        || string.IsNullOrWhiteSpace(Title) is false;
}
