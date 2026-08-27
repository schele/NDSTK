using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CategoryInferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly CookieCatalogue Catalogue = CookieCatalogue.Parse("""
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "durationDays": 0,
          "purpose": { "sv": "Inloggning.", "en": "Login." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
          "category": "statistics", "tracker": true, "durationDays": 730,
          "purpose": { "sv": "Mäter.", "en": "Measures." } },
        { "pattern": "_fbp", "provider": { "sv": "Meta", "en": "Meta" },
          "category": "marketing", "tracker": true, "durationDays": 90,
          "purpose": { "sv": "Annonser.", "en": "Adverts." } }
      ]
    }
    """);

    private static CookieDeclarationCandidate Classify(
        string name,
        ConsentPass pass,
        StorageKind storage = StorageKind.Cookie,
        DateTimeOffset? expires = null)
        => CategoryInference.Classify(
            new ObservedEntry(name, storage, pass, "https://ndstk.se/", expires),
            Catalogue,
            Now,
            Locale.Sv);

    // Nothing has been consented to in the first two passes, so anything set there is either
    // strictly necessary or a violation. An unrecognised cookie gets the benefit of the doubt on
    // category - the pass genuinely establishes it - but not on purpose.
    [Fact]
    public void An_unknown_cookie_in_the_undecided_pass_is_necessary()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.Undecided);

        Assert.Equal("necessary", candidate.Category);
        Assert.Equal(CandidateFlag.None, candidate.Flag);
    }

    [Theory]
    [InlineData(ConsentPass.Preferences, "preferences")]
    [InlineData(ConsentPass.Statistics, "statistics")]
    [InlineData(ConsentPass.Marketing, "marketing")]
    public void The_pass_that_first_shows_an_unknown_cookie_names_its_category(
        ConsentPass pass, string expected)
    {
        Assert.Equal(expected, Classify("mystery", pass).Category);
    }

    // This is the finding the whole design exists for: a tracker set despite a refusal.
    [Fact]
    public void A_tracker_in_the_reject_all_pass_is_a_violation()
    {
        CookieDeclarationCandidate candidate = Classify("_ga_ABC123", ConsentPass.RejectAll);

        Assert.Equal(CandidateFlag.Violation, candidate.Flag);
        Assert.Equal("statistics", candidate.Category);
    }

    [Fact]
    public void A_tracker_before_any_choice_exists_is_a_violation()
    {
        Assert.Equal(CandidateFlag.Violation, Classify("_fbp", ConsentPass.Undecided).Flag);
    }

    // The case a pass-1-and-2-only rule would have waved through: statistics was never granted in
    // the preferences pass, so a statistics cookie appearing there violates consent just as
    // plainly as one appearing after a flat refusal.
    [Fact]
    public void A_statistics_cookie_in_the_preferences_pass_is_a_violation()
    {
        Assert.Equal(CandidateFlag.Violation, Classify("_ga_ABC123", ConsentPass.Preferences).Flag);
    }

    [Fact]
    public void A_tracker_in_the_pass_that_granted_its_own_category_is_not_a_violation()
    {
        Assert.Equal(CandidateFlag.None, Classify("_ga_ABC123", ConsentPass.Statistics).Flag);
        Assert.Equal(CandidateFlag.None, Classify("_fbp", ConsentPass.Marketing).Flag);
    }

    [Fact]
    public void A_necessary_cookie_is_never_a_violation_in_any_pass()
    {
        foreach (ConsentPass pass in ConsentPasses.Comparable)
        {
            Assert.Equal(CandidateFlag.None, Classify("UMB_MEMBER", pass).Flag);
        }
    }

    // Accept-all grants everything, so nothing appearing there can be a violation - and an
    // unrecognised name there cannot be attributed to one category either.
    [Fact]
    public void An_unknown_cookie_first_seen_under_accept_all_needs_review()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.AcceptAll);

        Assert.Equal(CandidateFlag.NeedsReview, candidate.Flag);
        Assert.Equal("marketing", candidate.Category);
        Assert.Equal(Wording.NeedsReviewPurpose(Locale.Sv), candidate.Purpose);
    }

    [Fact]
    public void A_known_cookie_under_accept_all_is_neither_a_violation_nor_needs_review()
    {
        Assert.Equal(CandidateFlag.None, Classify("_ga_ABC123", ConsentPass.AcceptAll).Flag);
    }

    // A cookie that only exists behind a login is a session cookie by construction.
    [Fact]
    public void An_unknown_cookie_found_only_in_the_member_area_is_necessary()
    {
        CookieDeclarationCandidate candidate = Classify("member_thing", ConsentPass.MemberArea);

        Assert.Equal("necessary", candidate.Category);
        Assert.Equal(CandidateFlag.None, candidate.Flag);
    }

    // Two Google Analytics properties must not become two blocks. Collapsing the name onto the
    // catalogue pattern is what makes the merge idempotent for a whole family of cookies.
    [Fact]
    public void A_recognised_name_collapses_onto_its_catalogue_pattern()
    {
        Assert.Equal("_ga_*", Classify("_ga_ABC123", ConsentPass.Statistics).Name);
        Assert.Equal("_ga_*", Classify("_ga_XYZ789", ConsentPass.Statistics).Name);
    }

    [Fact]
    public void An_unrecognised_name_is_kept_verbatim()
    {
        Assert.Equal("mystery", Classify("mystery", ConsentPass.Undecided).Name);
    }

    [Fact]
    public void A_recognised_cookie_takes_the_catalogues_provider_purpose_and_duration()
    {
        CookieDeclarationCandidate candidate = Classify("_ga_ABC123", ConsentPass.Statistics);

        Assert.Equal("Google Analytics", candidate.Provider);
        Assert.Equal("Mäter.", candidate.Purpose);
        Assert.Equal("24 månader", candidate.Duration);
    }

    [Fact]
    public void An_unrecognised_cookie_takes_generated_wording_and_the_observed_duration()
    {
        CookieDeclarationCandidate candidate =
            Classify("mystery", ConsentPass.Statistics, expires: Now.AddDays(30));

        Assert.Equal(Wording.UnknownProvider(Locale.Sv), candidate.Provider);
        Assert.Equal(Wording.UnknownPurpose(Locale.Sv), candidate.Purpose);
        Assert.Equal("30 dagar", candidate.Duration);
    }

    // The storage type has to survive as one of the package dropdown's exact values, or the
    // endpoint rejects the declaration.
    [Theory]
    [InlineData(StorageKind.Cookie, "Cookie")]
    [InlineData(StorageKind.LocalStorage, "localStorage")]
    [InlineData(StorageKind.SessionStorage, "sessionStorage")]
    public void The_storage_type_is_written_as_the_dropdowns_own_value(
        StorageKind storage, string expected)
    {
        Assert.Equal(expected, Classify("mystery", ConsentPass.Undecided, storage).StorageType);
    }

    [Fact]
    public void The_first_seen_pass_and_url_are_carried_through_for_the_report()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.Marketing);

        Assert.Equal(ConsentPass.Marketing, candidate.FirstSeenPass);
        Assert.Equal("https://ndstk.se/", candidate.FirstSeenUrl);
    }
}
