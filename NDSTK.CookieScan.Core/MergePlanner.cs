namespace NDSTK.CookieScan.Core;

/// <summary>
/// Works out which proposed declarations are genuinely new, which are already on the page, and
/// what is worth telling a human about. Append-only by construction: there is no code path here
/// that removes or rewrites an existing declaration.
/// </summary>
public static class MergePlanner
{
    /// <summary>
    /// The most blocks one merge call may add. A backstop against a runaway scan bloating the
    /// node, not a paging limit - see <see cref="MergePlan.ExceedsCap"/>.
    /// </summary>
    public const int MaxBlocksPerCall = 50;

    public static MergePlan Plan(
        IEnumerable<CookieDeclarationCandidate> candidates,
        IEnumerable<string> declaredNames,
        CookieCatalogue catalogue)
    {
        // Blank declarations are editor noise. Left in, one would be read as a pattern, match
        // nothing, and land in DeclaredButNotFound on every run - or worse, if it were ever
        // treated as a wildcard, swallow every candidate silently.
        List<string> declared = declaredNames
            .Where(name => string.IsNullOrWhiteSpace(name) is false)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One candidate per name. Where the same collapsed pattern was seen in more than one
        // pass, the earliest wins: that is the observation carrying a violation, and losing it
        // would hide the finding the scan exists to make.
        List<CookieDeclarationCandidate> unique = candidates
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.FirstSeenPass).First())
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        List<CookieDeclarationCandidate> toAdd = unique
            .Where(candidate => declared.Any(name => CookieNameMatcher.EitherMatches(name, candidate.Name)) is false)
            .ToList();

        List<string> alreadyDeclared = declared
            .Where(name => unique.Any(candidate => CookieNameMatcher.EitherMatches(name, candidate.Name)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Reported, never deleted. A declaration can be entirely correct and simply not have been
        // triggered by this crawl - a booking POST, a page the cap cut off, a seasonal embed.
        List<string> declaredButNotFound = declared
            .Except(alreadyDeclared, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Only entries the catalogue flags as belonging to this site's own stack. An absent Google
        // cookie is normal; an absent antiforgery cookie means the crawl missed something.
        List<string> expectedButNotObserved = catalogue.Expected
            .Select(entry => entry.Pattern)
            .Where(pattern => unique.Any(candidate => CookieNameMatcher.EitherMatches(pattern, candidate.Name)) is false)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToList();

        return new MergePlan(toAdd, alreadyDeclared, declaredButNotFound, expectedButNotObserved);
    }
}
