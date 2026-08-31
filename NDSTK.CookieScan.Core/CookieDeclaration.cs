namespace NDSTK.CookieScan.Core;

/// <summary>
/// One row of a merge request: exactly the fields the endpoint writes into a
/// <c>cookieDefinition</c> block, and nothing about where they came from.
/// </summary>
/// <remarks>
/// Separate from <see cref="CookieDeclarationCandidate"/> because a declaration has two sources and
/// only one of them is an observation. A candidate carries the pass and URL it was first seen at,
/// which a catalogue-sourced declaration has no honest answer for - inventing a
/// <see cref="ConsentPass"/> so the two could share a type would put a sighting that never happened
/// into the record the report is written from.
/// </remarks>
public sealed record CookieDeclaration(
    string Name,
    string Provider,
    string Category,
    string Purpose,
    string Duration,
    string StorageType)
{
    /// <summary>The declaration for something the scan actually saw.</summary>
    public static CookieDeclaration From(CookieDeclarationCandidate candidate) => new(
        candidate.Name,
        candidate.Provider,
        candidate.Category,
        candidate.Purpose,
        candidate.Duration,
        candidate.StorageType);

    /// <summary>
    /// The declaration for a catalogue entry the scan did not see, but which the catalogue flags as
    /// belonging to this site's own stack.
    /// </summary>
    /// <remarks>
    /// <see cref="StorageKind.Cookie"/> is assumed: the catalogue has no storage column, and an
    /// <c>expected</c> entry is by definition part of the site's own server stack, which sets
    /// cookies rather than web storage. A catalogue that ever needs to expect a localStorage key
    /// will need that column before this line can be trusted.
    /// <para>
    /// No <c>expires</c> is passed. There is no browser sighting to read one from, so the entry's
    /// own <c>durationDays</c> is the whole answer - which is why an expected entry without one
    /// renders as the catalogue's undocumented-lifetime wording rather than as a guess.
    /// </para>
    /// </remarks>
    public static CookieDeclaration From(CatalogueEntry entry, DateTimeOffset now, Locale locale) => new(
        entry.Pattern,
        entry.Provider.For(locale),
        entry.Category,
        entry.Purpose.For(locale),
        DurationFormatter.Format(StorageKind.Cookie, entry.DurationDays, expires: null, now, locale),
        StorageKinds.ToWireName(StorageKind.Cookie));
}
