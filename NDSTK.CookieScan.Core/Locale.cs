using System.Text.Json.Serialization;

namespace NDSTK.CookieScan.Core;

/// <summary>The languages the scanner can write visitor-facing copy in.</summary>
public enum Locale
{
    Sv,
    En,
}

/// <summary>
/// A string in both shipped languages.
/// </summary>
/// <remarks>
/// Catalogue text ends up on a public policy page as legal wording, so it cannot be one language
/// with the other generated at runtime: "Denna webbplats" is not a translation job the scanner
/// should be doing. Both are written down and the locale picks one.
/// </remarks>
public sealed record LocalisedText(
    [property: JsonPropertyName("sv")] string Sv,
    [property: JsonPropertyName("en")] string En)
{
    public string For(Locale locale) => locale == Locale.Sv ? Sv : En;
}
