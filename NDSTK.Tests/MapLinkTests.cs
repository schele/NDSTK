using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class MapLinkTests
{
    [Fact]
    public void An_address_becomes_a_google_maps_search()
    {
        var url = MapLink.ForAddress("Lidingövägen 1, Stockholm");

        Assert.Equal(
            "https://www.google.com/maps/search/?api=1&query=Liding%C3%B6v%C3%A4gen%201%2C%20Stockholm",
            url);
    }

    // The address is a free-text field, so everything in it has to survive the trip: a comma, a
    // space and an ö all mean something different in a query string than they do on paper.
    [Fact]
    public void Swedish_characters_and_separators_are_escaped()
    {
        var url = MapLink.ForAddress("Åsögatan 5 & 7, Södermalm");

        Assert.DoesNotContain(" ", url);
        Assert.DoesNotContain("&7", url);
        Assert.Contains("%26", url);
        Assert.Contains("%C3%85", url);
    }

    // No address configured means no link, which is what leaves the location as plain text rather
    // than as an anchor pointing at a search for nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_address_means_no_link(string? address)
    {
        Assert.Null(MapLink.ForAddress(address));
    }

    // An editor who pastes an address out of a document brings the whitespace with it.
    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_encoded()
    {
        Assert.Equal(MapLink.ForAddress("GIH"), MapLink.ForAddress("  GIH \n"));
    }
}
