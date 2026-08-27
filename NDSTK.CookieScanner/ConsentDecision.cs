using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Records one consent decision against a browser context, and proves it landed.
/// </summary>
/// <remarks>
/// Shared by the six comparable passes and by the member dimension. It exists as one place
/// because the check below is easy to omit when the payload is retyped, and omitting it is
/// unrecoverable: a decision that returns 200 without setting a cookie leaves the scan measuring
/// the undecided state while reporting a decided one.
/// </remarks>
public static class ConsentDecision
{
    /// <summary>
    /// Posts the decision for <paramref name="pass"/> through the context's own request API, so
    /// the site writes the cookie server-side with the attributes it intends. Does nothing for a
    /// pass that has no decision to record.
    /// </summary>
    /// <remarks>
    /// Not <c>AddCookiesAsync</c>: a hand-forged cookie risks a shape the site rejects, and the
    /// scan would then silently measure the undecided state every time.
    /// </remarks>
    public static async Task RecordAsync(
        IBrowserContext context, Uri root, string endpointPath, ConsentPass pass)
    {
        object? decision = DecisionFor(pass);

        if (decision is null)
        {
            return;
        }

        string endpoint = new Uri(root, endpointPath).ToString();

        IAPIResponse response = await context.APIRequest.PostAsync(
            endpoint, new APIRequestContextOptions { DataObject = decision });

        if (response.Status == 429)
        {
            throw new InvalidOperationException(
                $"The consent endpoint throttled pass {pass} (HTTP 429). The passes must run "
                + "sequentially and the site's Esatto:CookieBanner:ThrottleRequestsPerMinute must "
                + "be at least 7. Raise it, or wait a minute and re-run.");
        }

        if (response.Ok is false)
        {
            throw new InvalidOperationException(
                $"The consent endpoint returned HTTP {response.Status} for pass {pass} at "
                + $"{endpoint}. Check that app.UseCookieConsent() is mapped and that EndpointPath "
                + "matches the site's configuration.");
        }

        // The jar was empty a moment ago - fresh context, nothing navigated - so the decision must
        // be sitting in it now. Fatal rather than logged: if it is not there, every later
        // observation would be attributed to a consent state that was never recorded, and the scan
        // would report a clean bill of health it never established. Checked by count rather than by
        // name, because the cookie's name is the site's own configuration.
        IReadOnlyList<BrowserContextCookiesResult> jar = await context.CookiesAsync();

        if (jar.Count == 0)
        {
            throw new InvalidOperationException(
                $"Pass {pass} posted its decision to {endpoint} and got HTTP {response.Status}, but "
                + "no cookie reached the browser context. Every later observation would be "
                + "attributed to a consent state that was never recorded. Check that the endpoint "
                + "sets the consent cookie on its response.");
        }
    }

    // accept-all sends the full category list explicitly: the package's endpoint grants exactly
    // the set it is given and deliberately does not read "all" from an omission.
    private static object? DecisionFor(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => null,
        ConsentPass.RejectAll => new { action = "reject-all", categories = Array.Empty<string>() },
        ConsentPass.Preferences => new { action = "custom", categories = new[] { "preferences" } },
        ConsentPass.Statistics => new { action = "custom", categories = new[] { "statistics" } },
        ConsentPass.Marketing => new { action = "custom", categories = new[] { "marketing" } },
        ConsentPass.AcceptAll or ConsentPass.MemberArea =>
            new { action = "accept-all", categories = new[] { "preferences", "statistics", "marketing" } },
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };
}
