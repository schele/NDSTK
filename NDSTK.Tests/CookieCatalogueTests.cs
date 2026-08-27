using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CookieCatalogueTests
{
    private const string Json = """
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "*", "provider": { "sv": "Okänd", "en": "Unknown" },
          "category": "marketing",
          "purpose": { "sv": "Okänt syfte.", "en": "Unknown purpose." } },
        { "pattern": "_ga*", "provider": { "sv": "Google", "en": "Google" },
          "category": "statistics", "tracker": true,
          "purpose": { "sv": "Bred.", "en": "Broad." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
          "category": "statistics", "tracker": true, "durationDays": 730,
          "purpose": { "sv": "Mäter.", "en": "Measures." } },
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "expected": true, "durationDays": 0,
          "purpose": { "sv": "Inloggning.", "en": "Login." } }
      ]
    }
    """;

    private static CookieCatalogue Catalogue() => CookieCatalogue.Parse(Json);

    // Three patterns match "_ga_ABC123". The most specific has to win, or every Google Analytics
    // property cookie is declared with the catch-all's wording and the wrong category.
    [Fact]
    public void The_most_specific_matching_pattern_wins()
    {
        CatalogueEntry? match = Catalogue().Match("_ga_ABC123");

        Assert.NotNull(match);
        Assert.Equal("_ga_*", match.Pattern);
        Assert.Equal("Google Analytics", match.Provider.Sv);
    }

    [Fact]
    public void A_looser_pattern_still_wins_when_it_is_the_only_one_that_fits()
    {
        CatalogueEntry? match = Catalogue().Match("_gat");

        Assert.NotNull(match);
        Assert.Equal("_ga*", match.Pattern);
    }

    [Fact]
    public void An_exact_pattern_wins_over_the_catch_all()
    {
        CatalogueEntry? match = Catalogue().Match("UMB_MEMBER");

        Assert.NotNull(match);
        Assert.Equal("UMB_MEMBER", match.Pattern);
        Assert.Equal("necessary", match.Category);
        Assert.False(match.Tracker);
    }

    [Fact]
    public void An_unmatched_name_falls_through_to_the_catch_all_when_one_exists()
    {
        CatalogueEntry? match = Catalogue().Match("totally_unknown_thing");

        Assert.NotNull(match);
        Assert.Equal("*", match.Pattern);
    }

    // A catalogue with no catch-all must return null rather than inventing a match: "unknown" is
    // what routes a cookie into the needs-review path instead of a confident wrong declaration.
    // The shipped catalogue deliberately has no catch-all, so this is the real code path.
    [Fact]
    public void A_catalogue_without_a_catch_all_returns_null_for_an_unknown_name()
    {
        CookieCatalogue catalogue = CookieCatalogue.Parse("""
        { "unknownCategory": "marketing", "entries": [
          { "pattern": "UMB_MEMBER", "provider": { "sv": "U", "en": "U" },
            "category": "necessary", "purpose": { "sv": "S", "en": "S" } } ] }
        """);

        Assert.Null(catalogue.Match("_fbp"));
    }

    // The report's "expected but not observed" section has nothing to draw on without this flag,
    // and it must exclude third-party entries: an absent Google cookie is normal, an absent
    // UMB_MEMBER on a site with a login is a finding.
    [Fact]
    public void Expected_selects_only_the_flagged_entries()
    {
        IReadOnlyList<CatalogueEntry> expected = Catalogue().Expected;

        Assert.Single(expected);
        Assert.Equal("UMB_MEMBER", expected[0].Pattern);
    }

    [Fact]
    public void Duration_days_is_read_when_present_and_null_when_absent()
    {
        Assert.Equal(730, Catalogue().Match("_ga_ABC")!.DurationDays);
        Assert.Equal(0, Catalogue().Match("UMB_MEMBER")!.DurationDays);
        Assert.Null(Catalogue().Match("_gat")!.DurationDays);
    }

    [Fact]
    public void Localised_text_resolves_per_locale()
    {
        CatalogueEntry entry = Catalogue().Match("_ga_ABC")!;

        Assert.Equal("Mäter.", entry.Purpose.For(Locale.Sv));
        Assert.Equal("Measures.", entry.Purpose.For(Locale.En));
    }

    [Fact]
    public void Unknown_category_is_read_from_the_document()
    {
        Assert.Equal("marketing", Catalogue().UnknownCategory);
    }

    // The embedded catalogue is what a fresh exe uses, so a typo in it is a shipping bug no other
    // test would catch.
    [Fact]
    public void The_embedded_default_catalogue_parses_and_knows_this_sites_stack()
    {
        CookieCatalogue catalogue = CookieCatalogue.Default();

        Assert.NotEmpty(catalogue.Entries);
        Assert.Equal("necessary", catalogue.Match("UMB_MEMBER")!.Category);
        Assert.Equal("necessary", catalogue.Match(".AspNetCore.Antiforgery.CfDJ8x")!.Category);
        Assert.Equal("necessary", catalogue.Match(".AspNetCore.Mvc.CookieTempDataProvider")!.Category);
        Assert.Equal("statistics", catalogue.Match("_ga_ABC123")!.Category);
        Assert.True(catalogue.Match("_ga_ABC123")!.Tracker);
    }

    // The shipped catalogue must have no catch-all, or nothing can ever reach needs-review.
    [Fact]
    public void The_embedded_catalogue_has_no_catch_all()
    {
        Assert.Null(CookieCatalogue.Default().Match("some_cookie_nobody_has_heard_of"));
    }

    // The TempData cookie is the one gap already known from reading the code, so it has to be
    // flagged expected or the report can never tell anyone it is missing.
    [Fact]
    public void The_embedded_catalogue_expects_the_temp_data_cookie()
    {
        Assert.Contains(
            CookieCatalogue.Default().Expected,
            entry => entry.Pattern == ".AspNetCore.Mvc.CookieTempDataProvider");
    }
}
