using System.Text;
using System.Text.Json;

namespace NDSTK.Consent;

/// <summary>
/// Builds the Google Consent Mode v2 <c>default</c>, <c>update</c> and tag <c>config</c> calls.
/// </summary>
/// <remarks>
/// The default call must run before any Google tag loads, which is why it is emitted inline in
/// <c>&lt;head&gt;</c> rather than from <c>consent.js</c>. Emitted only when a measurement id is
/// configured — see <see cref="ConsentOptions.GoogleMeasurementId"/>.
/// </remarks>
public static class ConsentModeScript
{
    private const string Preamble =
        "window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}";

    public static string Defaults() =>
        Preamble +
        "gtag('consent','default',{" +
        "'ad_storage':'denied'," +
        "'ad_user_data':'denied'," +
        "'ad_personalization':'denied'," +
        "'analytics_storage':'denied'," +
        "'functionality_storage':'denied'," +
        "'personalization_storage':'denied'," +
        "'wait_for_update':500});";

    public static string Update(IConsentState consent)
    {
        var marketing = Signal(consent.HasGranted(ConsentCategory.Marketing));
        var statistics = Signal(consent.HasGranted(ConsentCategory.Statistics));
        var preferences = Signal(consent.HasGranted(ConsentCategory.Preferences));

        return new StringBuilder()
            .Append("gtag('consent','update',{")
            .Append($"'ad_storage':'{marketing}',")
            .Append($"'ad_user_data':'{marketing}',")
            .Append($"'ad_personalization':'{marketing}',")
            .Append($"'analytics_storage':'{statistics}',")
            .Append($"'functionality_storage':'{preferences}',")
            .Append($"'personalization_storage':'{preferences}'")
            .Append("});")
            .ToString();
    }

    /// <summary>
    /// Registers the destination and fires the initial page view. Safe to emit unconditionally
    /// alongside <see cref="Defaults"/> and <see cref="Update"/>, even before - or if never - the
    /// actual gtag.js library loads: <c>gtag()</c> only pushes onto <c>dataLayer</c>, which the
    /// <see cref="Defaults"/> preamble defines regardless of whether the library is present, so this
    /// call simply waits in the queue until (and unless) the tag loads and replays it.
    /// </summary>
    public static string Config(string measurementId) =>
        // JsonSerializer's default encoder escapes '<', '>', '&' and other HTML-sensitive characters,
        // which is what makes it safe to splice a JSON string literal into an inline <script> block
        // without a separate JavaScript/HTML encoding step.
        $"gtag('js',new Date());gtag('config',{JsonSerializer.Serialize(measurementId)});";

    private static string Signal(bool granted) => granted ? "granted" : "denied";
}
