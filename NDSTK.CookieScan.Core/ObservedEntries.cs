namespace NDSTK.CookieScan.Core;

/// <summary>
/// Reduces every observation of a name across all passes to the single earliest one.
/// </summary>
/// <remarks>
/// A cookie set in the reject-all pass is still present in every later pass, so without this the
/// same cookie would be classified once per pass and the loosest classification could win. The
/// earliest appearance is the only one that carries information: it is the least consent under
/// which the site was still willing to set the thing.
/// </remarks>
public static class ObservedEntries
{
    public static IReadOnlyList<ObservedEntry> EarliestPerName(IEnumerable<ObservedEntry> entries)
        => entries
            .GroupBy(entry => (entry.Name.ToLowerInvariant(), entry.Storage))
            .Select(group => group.OrderBy(entry => entry.FirstSeenPass).First())
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
}
