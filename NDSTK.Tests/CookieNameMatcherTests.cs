using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CookieNameMatcherTests
{
    // The package seeds ".AspNetCore.Antiforgery.*" as a declaration, and ASP.NET Core appends a
    // random suffix to the real cookie. If a scan cannot recognise the pattern it re-adds the
    // cookie on every single run, which is the failure that makes a scanner worse than nothing.
    [Fact]
    public void A_pattern_matches_the_real_generated_name()
    {
        Assert.True(CookieNameMatcher.Matches(
            ".AspNetCore.Antiforgery.*", ".AspNetCore.Antiforgery.CfDJ8Nf_gA"));
    }

    [Fact]
    public void A_literal_name_never_matches_a_different_name()
    {
        Assert.False(CookieNameMatcher.Matches("UMB_MEMBER", "UMB_MEMBER_OTHER"));
        Assert.False(CookieNameMatcher.Matches("_ga", "_gat"));
    }

    [Fact]
    public void A_literal_name_matches_itself()
    {
        Assert.True(CookieNameMatcher.Matches("UMB_MEMBER", "UMB_MEMBER"));
    }

    // Cookie names are compared case-sensitively by browsers but declared by hand in the
    // backoffice. A casing near-miss should count as already declared, not as a new cookie.
    [Fact]
    public void Matching_ignores_case()
    {
        Assert.True(CookieNameMatcher.Matches("umb_member", "UMB_MEMBER"));
        Assert.True(CookieNameMatcher.Matches(".ASPNETCORE.ANTIFORGERY.*", ".aspnetcore.antiforgery.x"));
    }

    // The merge compares a found name against a declared one without knowing which side carries
    // the wildcard: the catalogue collapses onto patterns, but an editor may have typed a literal.
    [Fact]
    public void EitherMatches_works_whichever_side_carries_the_wildcard()
    {
        Assert.True(CookieNameMatcher.EitherMatches("_ga_*", "_ga_ABC123"));
        Assert.True(CookieNameMatcher.EitherMatches("_ga_ABC123", "_ga_*"));
        Assert.False(CookieNameMatcher.EitherMatches("_ga_*", "_fbp"));
    }

    [Fact]
    public void A_bare_wildcard_matches_anything_non_empty()
    {
        Assert.True(CookieNameMatcher.Matches("*", "anything"));
    }

    [Fact]
    public void Multiple_wildcards_are_supported()
    {
        Assert.True(CookieNameMatcher.Matches("_hj*Session*", "_hjFirstSessionUser"));
        Assert.False(CookieNameMatcher.Matches("_hj*Session*", "_hjUser"));
    }

    // A blank on either side is editor noise or a capture bug; it must never match, or one empty
    // declaration would swallow every found cookie and the scan would report nothing new forever.
    [Theory]
    [InlineData(null, "UMB_MEMBER")]
    [InlineData("", "UMB_MEMBER")]
    [InlineData("   ", "UMB_MEMBER")]
    [InlineData("UMB_MEMBER", null)]
    [InlineData("UMB_MEMBER", "")]
    public void A_blank_on_either_side_never_matches(string? pattern, string? name)
    {
        Assert.False(CookieNameMatcher.Matches(pattern, name));
    }

    // The catalogue picks between competing patterns by how much each leaves to a wildcard, so
    // "_ga_*" must beat "_ga*" must beat "*" for a real Google Analytics property cookie.
    [Fact]
    public void Wildcard_span_orders_competing_patterns_by_specificity()
    {
        const string name = "_ga_ABC123";

        int specific = CookieNameMatcher.WildcardCharCount("_ga_*", name);
        int looser = CookieNameMatcher.WildcardCharCount("_ga*", name);
        int loosest = CookieNameMatcher.WildcardCharCount("*", name);

        Assert.True(specific < looser);
        Assert.True(looser < loosest);
    }

    [Fact]
    public void Literal_prefix_length_breaks_a_tie()
    {
        Assert.Equal(4, CookieNameMatcher.LiteralPrefixLength("_ga_*"));
        Assert.Equal(0, CookieNameMatcher.LiteralPrefixLength("*"));
        Assert.Equal(10, CookieNameMatcher.LiteralPrefixLength("UMB_MEMBER"));
    }
}
