namespace NDSTK.Booking.Domain;

/// <summary>
/// Turns the club's address into a map link.
/// </summary>
/// <remarks>
/// The address is typed once on the Settings node; the text shown on a class is the court
/// ("GIH, bana 1", "Bana 2"), which is not something a map can find on its own. So the link points
/// at the address and the court stays the label.
///
/// A search URL rather than a pinned place: it is built from an address an editor can type and read
/// back, and both Google Maps apps hand a search over to the native map on a phone. A pinned link
/// would be an opaque string somebody has to fetch from Google first.
/// </remarks>
public static class MapLink
{
    private const string Search = "https://www.google.com/maps/search/?api=1&query=";

    /// <summary>The map link for an address, or null when no address is configured.</summary>
    public static string? ForAddress(string? address)
        => string.IsNullOrWhiteSpace(address)
            ? null
            : Search + Uri.EscapeDataString(address.Trim());
}
