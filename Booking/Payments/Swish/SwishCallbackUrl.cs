using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// The URL Swish posts the outcome to. Built from the application URL Umbraco already knows for
/// each environment, so no new setting can be wrong. Locally it names a host the simulator cannot
/// reach, and the page's poll settles the payment instead.
/// </summary>
public sealed class SwishCallbackUrl(IOptions<WebRoutingSettings> webRouting)
{
    public const string Path = "api/swish/callback";

    public string Build()
    {
        var applicationUrl = webRouting.Value.UmbracoApplicationUrl;
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            throw new InvalidOperationException(
                "Umbraco:CMS:WebRouting:UmbracoApplicationUrl must be set for the Swish callback URL.");
        }

        return new Uri(new Uri(applicationUrl.TrimEnd('/') + "/"), Path).ToString();
    }
}
