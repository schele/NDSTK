using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Where a scan's catalogue comes from, for every front end.
/// </summary>
/// <remarks>
/// Shared rather than duplicated per front end. If the window loaded the embedded catalogue while
/// the console tool honoured an override beside the exe, the same site would be scanned with
/// different categories and different Swedish wording depending on which one was used - exactly
/// the divergence <see cref="ScanRunner"/> exists to prevent.
/// <para>
/// Takes an <see cref="IScanLog"/> rather than writing to the console, so the window's notice
/// about an override lands in its log pane instead of nowhere.
/// </para>
/// </remarks>
public static class CatalogueSource
{
    /// <summary>The catalogue this run should use.</summary>
    public static CookieCatalogue Load(IScanLog log)
    {
        // An override beside the exe replaces the embedded catalogue wholesale, so legal wording can be
        // changed without a rebuild.
        string beside = Path.Combine(AppContext.BaseDirectory, "cookie-catalogue.json");

        if (File.Exists(beside))
        {
            log.Info($"Using the catalogue override at {beside}.");

            return CookieCatalogue.Parse(File.ReadAllText(beside));
        }

        return CookieCatalogue.Default();
    }
}
