using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CookieDeclarationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static CatalogueEntry Entry(int? durationDays = 0) => new(
        Pattern: ".AspNetCore.Mvc.CookieTempDataProvider",
        Provider: new LocalisedText("Denna webbplats", "This website"),
        Category: "necessary",
        Purpose: new LocalisedText(
            "Bär med sig ett meddelande till nästa sidvisning.",
            "Carries a message to the next page view."),
        DurationDays: durationDays,
        Tracker: false,
        Expected: true);

    // The mapping the write-back depends on: a catalogue row has to become a block the endpoint can
    // write without a sighting to draw the wording from.
    [Fact]
    public void A_catalogue_entry_becomes_a_declaration_in_the_requested_locale()
    {
        CookieDeclaration declaration = CookieDeclaration.From(Entry(), Now, Locale.Sv);

        Assert.Equal(".AspNetCore.Mvc.CookieTempDataProvider", declaration.Name);
        Assert.Equal("Denna webbplats", declaration.Provider);
        Assert.Equal("necessary", declaration.Category);
        Assert.Equal("Bär med sig ett meddelande till nästa sidvisning.", declaration.Purpose);
        Assert.Equal("Cookie", declaration.StorageType);
    }

    [Fact]
    public void An_english_run_takes_the_english_wording()
    {
        CookieDeclaration declaration = CookieDeclaration.From(Entry(), Now, Locale.En);

        Assert.Equal("This website", declaration.Provider);
        Assert.Equal("Carries a message to the next page view.", declaration.Purpose);
    }

    // Zero days is the catalogue's way of saying session, and there is no browser expiry to fall
    // back on for an entry nothing observed - so the duration has to come out of durationDays alone.
    [Fact]
    public void A_zero_day_entry_renders_as_a_session_cookie()
    {
        Assert.Equal(
            DurationFormatter.Format(StorageKind.Cookie, 0, null, Now, Locale.Sv),
            CookieDeclaration.From(Entry(), Now, Locale.Sv).Duration);
    }

    [Fact]
    public void An_entry_with_no_documented_lifetime_still_renders_something()
    {
        string duration = CookieDeclaration.From(Entry(durationDays: null), Now, Locale.Sv).Duration;

        Assert.False(string.IsNullOrWhiteSpace(duration));
    }

    // An observed candidate and a catalogue entry have to arrive at the endpoint indistinguishable:
    // the merge decides what to add by name, and a difference in shape between the two sources would
    // make one of them behave differently for reasons the operator could not see.
    [Fact]
    public void An_observed_candidate_keeps_every_field_it_had()
    {
        var candidate = new CookieDeclarationCandidate(
            Name: "ndstk-consent",
            Provider: "Denna webbplats",
            Category: "necessary",
            Purpose: "Sparar dina cookieval.",
            Duration: "12 månader",
            StorageType: "Cookie",
            Flag: CandidateFlag.None,
            FirstSeenPass: ConsentPass.RejectAll,
            FirstSeenUrl: "https://ndstk.se/");

        CookieDeclaration declaration = CookieDeclaration.From(candidate);

        Assert.Equal("ndstk-consent", declaration.Name);
        Assert.Equal("Denna webbplats", declaration.Provider);
        Assert.Equal("necessary", declaration.Category);
        Assert.Equal("Sparar dina cookieval.", declaration.Purpose);
        Assert.Equal("12 månader", declaration.Duration);
        Assert.Equal("Cookie", declaration.StorageType);
    }
}
