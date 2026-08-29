namespace NDSTK.CookieScan.Core;

/// <summary>A cookie that was declared under one category and is now under another.</summary>
public sealed record CategoryChange(string Name, string From, string To);

/// <summary>
/// What changed between two scans of the same site.
/// </summary>
/// <remarks>
/// Matched by name, case-insensitively, and deliberately NOT as globs. Two scans of one site draw
/// their names from the same catalogue, so a pattern in one run is a pattern in the other; glob
/// matching would report a pattern and a literal that happen to overlap as unchanged, hiding the
/// case where one genuinely replaced the other.
/// </remarks>
public sealed record ScanDiff(
    IReadOnlyList<CookieDeclarationCandidate> Appeared,
    IReadOnlyList<CookieDeclarationCandidate> Disappeared,
    IReadOnlyList<CategoryChange> Recategorised)
{
    public static ScanDiff Between(
        IReadOnlyList<CookieDeclarationCandidate> older,
        IReadOnlyList<CookieDeclarationCandidate> newer)
    {
        Dictionary<string, CookieDeclarationCandidate> before =
            Index(older);
        Dictionary<string, CookieDeclarationCandidate> after =
            Index(newer);

        List<CookieDeclarationCandidate> appeared =
            [.. after.Where(entry => before.ContainsKey(entry.Key) is false).Select(entry => entry.Value)];

        List<CookieDeclarationCandidate> disappeared =
            [.. before.Where(entry => after.ContainsKey(entry.Key) is false).Select(entry => entry.Value)];

        List<CategoryChange> recategorised = [];

        foreach ((string key, CookieDeclarationCandidate was) in before)
        {
            if (after.TryGetValue(key, out CookieDeclarationCandidate? now)
                && string.Equals(was.Category, now.Category, StringComparison.Ordinal) is false)
            {
                recategorised.Add(new CategoryChange(now.Name, was.Category, now.Category));
            }
        }

        return new ScanDiff(
            [.. appeared.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)],
            [.. disappeared.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)],
            [.. recategorised.OrderBy(change => change.Name, StringComparer.Ordinal)]);
    }

    // Last one wins on a duplicate name, which cannot happen from a real scan - the runner already
    // reduces to one candidate per name - but a hand-edited history file should not throw.
    private static Dictionary<string, CookieDeclarationCandidate> Index(
        IReadOnlyList<CookieDeclarationCandidate> candidates)
    {
        Dictionary<string, CookieDeclarationCandidate> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (CookieDeclarationCandidate candidate in candidates)
        {
            index[candidate.Name] = candidate;
        }

        return index;
    }
}
