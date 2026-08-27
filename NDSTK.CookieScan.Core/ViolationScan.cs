namespace NDSTK.CookieScan.Core;

/// <summary>
/// Finds every observation that was set without the consent its category needed.
/// </summary>
/// <remarks>
/// Runs over the RAW observations rather than the earliest-per-name reduction that
/// <see cref="ObservedEntries.EarliestPerName"/> produces, because a violation is a property of
/// one sighting rather than of a name. A cookie whose category was granted in the pass that first
/// set it, and which is then set again in a pass that granted something else, is a violation on
/// that second sighting only - and reducing to the earliest sighting first would discard it.
/// <para>
/// Declarations still come from the reduced list: a policy page wants one row per cookie, and the
/// earliest sighting is the one whose category is best evidenced.
/// </para>
/// </remarks>
public static class ViolationScan
{
    /// <summary>
    /// One entry per (name, offending pass), ordered by name then pass so two runs over the same
    /// site report the same thing in the same order.
    /// </summary>
    public static IReadOnlyList<CookieDeclarationCandidate> Find(
        IEnumerable<ObservedEntry> allObservations,
        CookieCatalogue catalogue,
        DateTimeOffset now,
        Locale locale)
        => allObservations
            .Select(observation => CategoryInference.Classify(observation, catalogue, now, locale))
            .Where(candidate => candidate.Flag == CandidateFlag.Violation)
            .GroupBy(candidate => (candidate.Name.ToLowerInvariant(), candidate.FirstSeenPass))
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FirstSeenPass)
            .ToArray();
}
