namespace NDSTK.CookieScan.Core;

/// <summary>
/// Copy the scanner writes itself, for a cookie the catalogue does not recognise.
/// </summary>
/// <remarks>
/// This text lands on a public policy page, so it says plainly that a human has not checked it
/// yet. Inventing a plausible purpose would be worse than admitting there isn't one: a visitor
/// reading a confident sentence has no way to know it was guessed.
/// </remarks>
public static class Wording
{
    public static string UnknownProvider(Locale locale)
        => locale == Locale.Sv ? "Okänd" : "Unknown";

    /// <summary>For a cookie whose category a pass established but whose purpose is unknown.</summary>
    public static string UnknownPurpose(Locale locale)
        => locale == Locale.Sv
            ? "Hittad av cookieskannern. Syftet är inte fastställt än."
            : "Found by the cookie scanner. Its purpose has not been established yet.";

    /// <summary>For a cookie no pass could attribute, so neither purpose nor category is settled.</summary>
    public static string NeedsReviewPurpose(Locale locale)
        => locale == Locale.Sv
            ? "Hittad av cookieskannern. Både syfte och kategori behöver kontrolleras."
            : "Found by the cookie scanner. Both its purpose and its category need checking.";
}
