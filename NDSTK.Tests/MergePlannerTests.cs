using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class MergePlannerTests
{
    private static readonly CookieCatalogue EmptyCatalogue =
        CookieCatalogue.Parse("""{ "unknownCategory": "marketing", "entries": [] }""");

    private static readonly CookieCatalogue ExpectingUmbMember = CookieCatalogue.Parse("""
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "expected": true,
          "purpose": { "sv": "Inloggning.", "en": "Login." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google", "en": "Google" },
          "category": "statistics", "tracker": true,
          "purpose": { "sv": "Mäter.", "en": "Measures." } }
      ]
    }
    """);

    private static CookieDeclarationCandidate Candidate(
        string name,
        string category = "necessary",
        CandidateFlag flag = CandidateFlag.None,
        ConsentPass pass = ConsentPass.Undecided)
        => new(
            Name: name,
            Provider: "Denna webbplats",
            Category: category,
            Purpose: "Syfte.",
            Duration: "Session",
            StorageType: "Cookie",
            Flag: flag,
            FirstSeenPass: pass,
            FirstSeenUrl: "https://ndstk.se/");

    [Fact]
    public void A_brand_new_cookie_is_added()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("newcookie")], [], EmptyCatalogue);

        Assert.Single(plan.ToAdd);
        Assert.Equal("newcookie", plan.ToAdd[0].Name);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void An_already_declared_cookie_is_not_added_again()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], ["UMB_MEMBER"], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Contains("UMB_MEMBER", plan.AlreadyDeclared);
        Assert.False(plan.HasWork);
    }

    // The package seeds ".AspNetCore.Antiforgery.*". ASP.NET Core sets a suffixed real cookie. If
    // the existing pattern does not swallow it, every run re-adds the same cookie forever.
    [Fact]
    public void A_cookie_covered_by_an_existing_pattern_is_not_added()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate(".AspNetCore.Antiforgery.CfDJ8Nf")],
            [".AspNetCore.Antiforgery.*"],
            EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Contains(".AspNetCore.Antiforgery.*", plan.AlreadyDeclared);
    }

    [Fact]
    public void Matching_an_existing_declaration_ignores_case()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("umb_member")], ["UMB_MEMBER"], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
    }

    // Two observations of the same collapsed pattern - two Google Analytics properties - must
    // become one block, not two identical ones.
    [Fact]
    public void Two_candidates_with_the_same_name_collapse_to_one()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate("_ga_*", "statistics"), Candidate("_ga_*", "statistics")],
            [],
            EmptyCatalogue);

        Assert.Single(plan.ToAdd);
    }

    // When the same pattern was seen in two passes, the earlier one wins - because that is the
    // one carrying the violation. Dropping it would hide the finding the scan exists to make.
    [Fact]
    public void Collapsing_keeps_the_earliest_pass_so_a_violation_survives()
    {
        MergePlan plan = MergePlanner.Plan(
            [
                Candidate("_ga_*", "statistics", CandidateFlag.None, ConsentPass.Statistics),
                Candidate("_ga_*", "statistics", CandidateFlag.Violation, ConsentPass.RejectAll),
            ],
            [],
            EmptyCatalogue);

        Assert.Single(plan.ToAdd);
        Assert.Equal(CandidateFlag.Violation, plan.ToAdd[0].Flag);
        Assert.Equal(ConsentPass.RejectAll, plan.ToAdd[0].FirstSeenPass);
    }

    // Reported, never deleted: a declaration can be perfectly correct and simply not have been
    // triggered by this crawl.
    [Fact]
    public void A_declaration_nothing_matched_is_reported_as_possibly_stale()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], ["UMB_MEMBER", "old-cookie"], EmptyCatalogue);

        Assert.Contains("old-cookie", plan.DeclaredButNotFound);
        Assert.DoesNotContain("UMB_MEMBER", plan.DeclaredButNotFound);
    }

    [Fact]
    public void An_expected_catalogue_entry_the_scan_never_saw_is_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("something_else")], [], ExpectingUmbMember);

        Assert.Contains("UMB_MEMBER", plan.ExpectedButNotObserved);
    }

    [Fact]
    public void An_expected_entry_the_scan_did_see_is_not_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], [], ExpectingUmbMember);

        Assert.Empty(plan.ExpectedButNotObserved);
    }

    // Only entries flagged expected count. An absent Google cookie is normal, not a finding.
    [Fact]
    public void An_unflagged_catalogue_entry_the_scan_never_saw_is_not_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], [], ExpectingUmbMember);

        Assert.DoesNotContain("_ga_*", plan.ExpectedButNotObserved);
    }

    [Fact]
    public void Fifty_new_declarations_are_within_the_cap()
    {
        IReadOnlyList<CookieDeclarationCandidate> candidates =
            Enumerable.Range(0, 50).Select(index => Candidate($"cookie{index}")).ToArray();

        MergePlan plan = MergePlanner.Plan(candidates, [], EmptyCatalogue);

        Assert.Equal(50, plan.ToAdd.Count);
        Assert.False(plan.ExceedsCap);
    }

    // Past the cap the endpoint refuses outright rather than writing the first fifty: a partial
    // apply leaves the page in a state nobody chose and makes the next run's diff meaningless.
    [Fact]
    public void Fifty_one_new_declarations_exceed_the_cap_without_being_truncated()
    {
        IReadOnlyList<CookieDeclarationCandidate> candidates =
            Enumerable.Range(0, 51).Select(index => Candidate($"cookie{index}")).ToArray();

        MergePlan plan = MergePlanner.Plan(candidates, [], EmptyCatalogue);

        Assert.Equal(51, plan.ToAdd.Count);
        Assert.True(plan.ExceedsCap);
    }

    [Fact]
    public void Nothing_found_and_nothing_declared_is_an_empty_plan()
    {
        MergePlan plan = MergePlanner.Plan([], [], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.AlreadyDeclared);
        Assert.Empty(plan.DeclaredButNotFound);
        Assert.False(plan.HasWork);
        Assert.False(plan.ExceedsCap);
    }

    // A blank declaration on the page is editor noise. It must not be treated as a pattern, or it
    // would swallow every candidate and the scan would silently report nothing new forever.
    [Fact]
    public void A_blank_existing_declaration_does_not_swallow_every_candidate()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("newcookie")], ["", "   "], EmptyCatalogue);

        Assert.Single(plan.ToAdd);
    }

    [Fact]
    public void The_added_list_is_ordered_deterministically_by_name()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate("zebra"), Candidate("alpha"), Candidate("mid")],
            [],
            EmptyCatalogue);

        Assert.Equal(["alpha", "mid", "zebra"], plan.ToAdd.Select(candidate => candidate.Name));
    }

    // The write-back's second source. These entries are the catalogue's statement that this site's
    // own stack sets them, so a run that could not reach one still declares it - the crawl issues
    // only GETs, and a cookie written by a booking POST can never appear in the observations.
    [Fact]
    public void An_expected_entry_the_scan_missed_is_returned_whole()
    {
        IReadOnlyList<CatalogueEntry> unobserved =
            MergePlanner.UnobservedExpected([Candidate("_ga_1234")], ExpectingUmbMember);

        CatalogueEntry entry = Assert.Single(unobserved);

        Assert.Equal("UMB_MEMBER", entry.Pattern);
        Assert.Equal("necessary", entry.Category);
        Assert.Equal("Inloggning.", entry.Purpose.For(Locale.Sv));
    }

    [Fact]
    public void An_expected_entry_the_scan_saw_is_not_returned()
    {
        Assert.Empty(MergePlanner.UnobservedExpected([Candidate("UMB_MEMBER")], ExpectingUmbMember));
    }

    // Only the flagged ones. An absent Google cookie is normal - the site may simply not load it any
    // more - and declaring one on the catalogue's word would put a cookie on the policy page that
    // nothing sets.
    [Fact]
    public void An_unflagged_catalogue_entry_is_never_returned()
    {
        IReadOnlyList<CatalogueEntry> unobserved =
            MergePlanner.UnobservedExpected([], ExpectingUmbMember);

        Assert.Equal(["UMB_MEMBER"], unobserved.Select(entry => entry.Pattern));
    }

    // Wildcards match here exactly as they do everywhere else: a site that set _ga_G-ABC accounts
    // for a _ga_* expectation, and must not have it declared a second time.
    [Fact]
    public void A_wildcard_expectation_is_accounted_for_by_a_matching_sighting()
    {
        CookieCatalogue expectingWildcard = CookieCatalogue.Parse("""
        {
          "unknownCategory": "marketing",
          "entries": [
            { "pattern": ".AspNetCore.Antiforgery.*", "provider": { "sv": "Denna webbplats", "en": "This website" },
              "category": "necessary", "expected": true,
              "purpose": { "sv": "Skyddar formulär.", "en": "Protects forms." } }
          ]
        }
        """);

        Assert.Empty(MergePlanner.UnobservedExpected(
            [Candidate(".AspNetCore.Antiforgery.WJz1p0")], expectingWildcard));
    }
}
