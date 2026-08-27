namespace NDSTK.CookieScan.Core;

/// <summary>
/// Matches cookie names against declaration patterns, where <c>*</c> is the only wildcard.
/// </summary>
/// <remarks>
/// The CookieBanner package seeds pattern declarations - <c>.AspNetCore.Antiforgery.*</c> - and
/// ASP.NET Core appends a random suffix to the real cookie, so a found name has to be recognisable
/// by the pattern already on the page. Without that, every scan re-adds a cookie that is already
/// declared, and the tool actively makes the policy page worse.
/// <para>
/// Case-insensitive on purpose. Browsers compare cookie names case-sensitively, but declarations
/// are typed by hand, and a casing near-miss should count as declared rather than as new.
/// </para>
/// </remarks>
public static class CookieNameMatcher
{
    /// <summary>
    /// True when <paramref name="name"/> matches <paramref name="pattern"/>. A blank on either
    /// side is never a match: one empty declaration would otherwise swallow every found cookie.
    /// </summary>
    public static bool Matches(string? pattern, string? name)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return IsMatch(pattern, name);
    }

    /// <summary>
    /// True when either string, read as a pattern, matches the other. The merge compares a found
    /// name against a declared one without knowing which of the two carries the wildcard.
    /// </summary>
    public static bool EitherMatches(string? a, string? b)
        => Matches(a, b) || Matches(b, a);

    /// <summary>
    /// How many characters of <paramref name="name"/> the pattern's wildcards had to absorb.
    /// Lower is more specific, which is how the catalogue chooses between two matching entries.
    /// </summary>
    public static int WildcardCharCount(string pattern, string name)
    {
        int wildcards = pattern.Count(character => character == '*');
        int literals = pattern.Length - wildcards;

        return name.Length - literals;
    }

    /// <summary>
    /// Characters before the first wildcard. The tie-break when two patterns absorb the same
    /// number of characters.
    /// </summary>
    public static int LiteralPrefixLength(string pattern)
    {
        int star = pattern.IndexOf('*', StringComparison.Ordinal);

        return star < 0 ? pattern.Length : star;
    }

    // Iterative glob rather than a translated Regex: the pattern comes from an editable JSON
    // catalogue and from hand-typed declarations, so a stray '(' or '+' must be a literal
    // character rather than a regex construct - or worse, a parse exception mid-scan.
    private static bool IsMatch(string pattern, string name)
    {
        int patternIndex = 0;
        int nameIndex = 0;
        int lastStar = -1;
        int nameAtLastStar = 0;

        while (nameIndex < name.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                lastStar = patternIndex++;
                nameAtLastStar = nameIndex;
            }
            else if (patternIndex < pattern.Length && SameCharacter(pattern[patternIndex], name[nameIndex]))
            {
                patternIndex++;
                nameIndex++;
            }
            else if (lastStar >= 0)
            {
                // Backtrack: let the last wildcard absorb one more character and try again.
                patternIndex = lastStar + 1;
                nameIndex = ++nameAtLastStar;
            }
            else
            {
                return false;
            }
        }

        // Trailing wildcards may legitimately match nothing at all.
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool SameCharacter(char pattern, char name)
        => char.ToLowerInvariant(pattern) == char.ToLowerInvariant(name);
}
