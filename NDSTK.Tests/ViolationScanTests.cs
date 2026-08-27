using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class ViolationScanTests
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
          "purpose": { "sv": "Annonser.", "en": "Adverts." } },
        { "pattern": "cookie_prefs", "provider": { "sv": "Denna webbplats", "en": "This website" },
          "category": "preferences", "durationDays": 365,
          "purpose": { "sv": "Sparar val.", "en": "Saves choices." } }
      ]
    }
    """);

    private static ObservedEntry Observe(string name, ConsentPass pass, string url = "https://ndstk.se/")
        => new(name, StorageKind.Cookie, pass, url, null);

    // Mirrors the reduction NDSTK.CookieScan.Core.ObservedEntries.EarliestPerName will perform
    // once it exists (see the plan's Task 8) - not yet part of this project, so reproduced
    // locally here to pin exactly the defect this fix addresses: dedupe-then-classify hides a
    // violation that a raw scan over every observation catches.
    private static IReadOnlyList<ObservedEntry> EarliestPerName(IEnumerable<ObservedEntry> observations)
        => observations
            .GroupBy(observation => observation.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(observation => observation.FirstSeenPass).First())
            .ToArray();

    // The headline case: a tracker set before any consent was granted.
    [Fact]
    public void A_tracker_set_under_reject_all_is_found()
    {
        IReadOnlyList<CookieDeclarationCandidate> violations =
            ViolationScan.Find([Observe("_ga_ABC123", ConsentPass.RejectAll)], Catalogue, Now, Locale.Sv);

        Assert.Single(violations);
        Assert.Equal("_ga_*", violations[0].Name);
        Assert.Equal(ConsentPass.RejectAll, violations[0].FirstSeenPass);
    }

    // The case this fix exists for: a preferences-category cookie granted in the Preferences pass,
    // and set again in the Marketing pass - where preferences was never granted. Dedupe alone
    // would keep only the granted sighting and report the cookie clean.
    [Fact]
    public void A_cookie_granted_in_one_pass_and_set_again_in_another_is_a_violation_on_the_second_sighting()
    {
        ObservedEntry grantedSighting = Observe("cookie_prefs", ConsentPass.Preferences);
        ObservedEntry violatingSighting = Observe("cookie_prefs", ConsentPass.Marketing);

        IReadOnlyList<CookieDeclarationCandidate> violations =
            ViolationScan.Find([grantedSighting, violatingSighting], Catalogue, Now, Locale.Sv);

        Assert.Single(violations);
        Assert.Equal(ConsentPass.Marketing, violations[0].FirstSeenPass);

        // Documents precisely what would otherwise be missed: reducing to the earliest sighting
        // first, then classifying just that one, reports this cookie clean.
        CookieDeclarationCandidate reduced = CategoryInference.Classify(
            EarliestPerName([grantedSighting, violatingSighting]).Single(), Catalogue, Now, Locale.Sv);

        Assert.Equal(CandidateFlag.None, reduced.Flag);
    }

    [Fact]
    public void A_necessary_cookie_observed_in_every_pass_yields_no_violations()
    {
        ObservedEntry[] observations = Enum.GetValues<ConsentPass>()
            .Select(pass => Observe("UMB_MEMBER", pass))
            .ToArray();

        Assert.Empty(ViolationScan.Find(observations, Catalogue, Now, Locale.Sv));
    }

    [Fact]
    public void An_observation_under_accept_all_never_yields_a_violation()
    {
        IReadOnlyList<CookieDeclarationCandidate> violations =
            ViolationScan.Find([Observe("_ga_ABC123", ConsentPass.AcceptAll)], Catalogue, Now, Locale.Sv);

        Assert.Empty(violations);
    }

    // The same cookie set without consent in two different passes breaks two different promises,
    // so it is two findings, not one deduplicated one.
    [Fact]
    public void The_same_cookie_violating_under_two_passes_yields_two_findings()
    {
        IReadOnlyList<CookieDeclarationCandidate> violations = ViolationScan.Find(
            [Observe("_ga_ABC123", ConsentPass.RejectAll), Observe("_ga_ABC123", ConsentPass.Preferences)],
            Catalogue,
            Now,
            Locale.Sv);

        Assert.Equal(2, violations.Count);
        Assert.Equal(
            [ConsentPass.RejectAll, ConsentPass.Preferences],
            violations.Select(violation => violation.FirstSeenPass));
    }

    [Fact]
    public void An_empty_input_yields_an_empty_result()
    {
        Assert.Empty(ViolationScan.Find([], Catalogue, Now, Locale.Sv));
    }

    [Fact]
    public void Shuffled_input_yields_the_same_ordered_output()
    {
        ObservedEntry[] observations =
        [
            Observe("_fbp", ConsentPass.Undecided),
            Observe("_ga_ABC123", ConsentPass.RejectAll),
            Observe("cookie_prefs", ConsentPass.Marketing),
        ];

        IReadOnlyList<CookieDeclarationCandidate> forward =
            ViolationScan.Find(observations, Catalogue, Now, Locale.Sv);
        IReadOnlyList<CookieDeclarationCandidate> reversed =
            ViolationScan.Find(observations.Reverse(), Catalogue, Now, Locale.Sv);

        Assert.Equal(
            forward.Select(violation => (violation.Name, violation.FirstSeenPass)),
            reversed.Select(violation => (violation.Name, violation.FirstSeenPass)));
    }
}
