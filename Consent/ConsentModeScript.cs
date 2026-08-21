using System.Text;

namespace NDSTK.Consent;

/// <summary>
/// Builds the Google Consent Mode v2 <c>default</c> and <c>update</c> calls.
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

    private static string Signal(bool granted) => granted ? "granted" : "denied";
}
