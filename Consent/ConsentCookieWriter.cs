using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace NDSTK.Consent;

/// <summary>
/// Turns a validated request into a decision and writes the cookie.
/// </summary>
/// <remarks>
/// The cookie is written here, server-side, rather than by JavaScript. That is what guarantees the
/// attributes are correct — lifetime, SameSite, and Secure tracking the actual scheme.
/// </remarks>
public sealed class ConsentCookieWriter(IOptions<ConsentOptions> options)
{
    /// <summary>Known action names, mapped explicitly so an unrecognised value is a hard failure.</summary>
    public static bool TryParseAction(string? action, out ConsentAction parsed)
    {
        switch (action)
        {
            case "accept-all": parsed = ConsentAction.AcceptAll; return true;
            case "reject-all": parsed = ConsentAction.RejectAll; return true;
            case "custom": parsed = ConsentAction.Custom; return true;
            case "withdrawn": parsed = ConsentAction.Withdrawn; return true;
            default: parsed = default; return false;
        }
    }

    public ConsentDecision Write(HttpResponse response, ConsentRequest request)
    {
        ConsentOptions settings = options.Value;

        var granted = new HashSet<ConsentCategory>();
        foreach (var name in request.Categories ?? [])
        {
            // Necessary is implied, never client-supplied; unknown names are discarded.
            if (ConsentCategories.TryParse(name, out ConsentCategory category)
                && category != ConsentCategory.Necessary)
            {
                granted.Add(category);
            }
        }

        var decision = new ConsentDecision(
            settings.PolicyVersion,
            DateTimeOffset.UtcNow,
            granted,
            ConsentCookieCodec.NewConsentId());

        response.Cookies.Append(settings.CookieName, ConsentCookieCodec.Encode(decision), new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            HttpOnly = false, // the banner must read this to unblock scripts without a reload
            Secure = response.HttpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(settings.CookieLifetimeDays),
            IsEssential = true,
        });

        return decision;
    }
}
