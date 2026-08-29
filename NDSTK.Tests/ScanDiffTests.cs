using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class ScanDiffTests
{
    private static CookieDeclarationCandidate Candidate(string name, string category = "necessary")
        => new(name, "Denna webbplats", category, "Syfte.", "Session", "Cookie",
            CandidateFlag.None, ConsentPass.Undecided, "https://ndstk.se/");

    // The question the whole history feature exists to answer: what turned up after that deploy?
    [Fact]
    public void A_cookie_only_in_the_newer_scan_appeared()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a")], [Candidate("a"), Candidate("_ga_*")]);

        Assert.Single(diff.Appeared);
        Assert.Equal("_ga_*", diff.Appeared[0].Name);
        Assert.Empty(diff.Disappeared);
        Assert.Empty(diff.Recategorised);
    }

    [Fact]
    public void A_cookie_only_in_the_older_scan_disappeared()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a"), Candidate("old")], [Candidate("a")]);

        Assert.Single(diff.Disappeared);
        Assert.Equal("old", diff.Disappeared[0].Name);
        Assert.Empty(diff.Appeared);
    }

    // A cookie changing category between runs means the site changed what it does with it, which
    // is a more interesting finding than either list.
    [Fact]
    public void A_cookie_whose_category_changed_is_reported_with_both_categories()
    {
        ScanDiff diff = ScanDiff.Between(
            [Candidate("x", "necessary")], [Candidate("x", "marketing")]);

        Assert.Single(diff.Recategorised);
        Assert.Equal("x", diff.Recategorised[0].Name);
        Assert.Equal("necessary", diff.Recategorised[0].From);
        Assert.Equal("marketing", diff.Recategorised[0].To);
        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
    }

    [Fact]
    public void Two_identical_scans_produce_three_empty_lists()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a"), Candidate("b")], [Candidate("b"), Candidate("a")]);

        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
        Assert.Empty(diff.Recategorised);
    }

    [Fact]
    public void Everything_appeared_when_the_older_scan_is_empty()
    {
        ScanDiff diff = ScanDiff.Between([], [Candidate("a"), Candidate("b")]);

        Assert.Equal(2, diff.Appeared.Count);
        Assert.Empty(diff.Disappeared);
    }

    [Fact]
    public void Matching_names_ignores_case_like_the_rest_of_the_codebase()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("UMB_MEMBER")], [Candidate("umb_member")]);

        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
    }

    // Deliberately NOT glob matching. Two scans of the same site draw names from the same
    // catalogue, so a pattern in one is a pattern in the other; treating them as globs would
    // report a pattern and a literal that happen to overlap as unchanged when one genuinely
    // replaced the other.
    [Fact]
    public void A_pattern_and_a_name_it_would_match_are_treated_as_different_cookies()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("_ga_*")], [Candidate("_ga_ABC123")]);

        Assert.Single(diff.Appeared);
        Assert.Single(diff.Disappeared);
    }

    [Fact]
    public void All_three_lists_are_ordered_by_name()
    {
        ScanDiff diff = ScanDiff.Between([], [Candidate("zebra"), Candidate("alpha"), Candidate("mid")]);

        Assert.Equal(["alpha", "mid", "zebra"], diff.Appeared.Select(candidate => candidate.Name));
    }
}
