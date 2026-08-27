namespace NDSTK.CookieScan.Core;

/// <summary>
/// One cookie or storage key a scan actually saw, reduced to what the rules need.
/// </summary>
/// <remarks>
/// Free of Playwright types on purpose: that is what lets category inference be unit tested
/// without launching a browser.
/// <paramref name="Expires"/> is null for a session cookie and for every storage entry.
/// </remarks>
public sealed record ObservedEntry(
    string Name,
    StorageKind Storage,
    ConsentPass FirstSeenPass,
    string FirstSeenUrl,
    DateTimeOffset? Expires);
