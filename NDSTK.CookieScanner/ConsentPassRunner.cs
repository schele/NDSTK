using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>One thing a pass found, with the URL it first turned up on.</summary>
public sealed record PassEntry(string Name, StorageKind Storage, DateTimeOffset? Expires, Uri FirstUrl);

/// <summary>Everything one pass produced.</summary>
public sealed record PassResult(
    ConsentPass Pass,
    IReadOnlyList<PassEntry> Entries,
    IReadOnlySet<string> Hosts);

/// <summary>
/// Runs one consent pass: a clean browser context, a real decision posted to the site, then the
/// fixed URL list replayed with everything the browser holds read back after each page.
/// </summary>
public sealed class ConsentPassRunner(IBrowser browser, ScanOptions options, string endpointPath)
{
    public async Task<PassResult> RunAsync(ConsentPass pass, IReadOnlyList<Uri> urls)
    {
        // A fresh context per pass is what makes "first seen in this pass" mean anything: the
        // cookie jar starts empty, so nothing carries over from the pass before.
        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        IPage page = await context.NewPageAsync();

        PageCapture.RecordHosts(page, hosts, options.Url);

        await DecideAsync(context, pass);

        Dictionary<(string Name, StorageKind Storage), PassEntry> found = [];

        foreach (Uri url in urls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                // First URL wins - it is the page that actually caused the thing to be set, and
                // that is what makes a report line actionable.
                found.TryAdd(
                    (entry.Name, entry.Storage),
                    new PassEntry(entry.Name, entry.Storage, entry.Expires, url));
            }
        }

        return new PassResult(pass, [.. found.Values], hosts);
    }

    /// <summary>
    /// Posts the pass's decision to the site's own consent endpoint.
    /// </summary>
    /// <remarks>
    /// Through the context's API request, not <c>AddCookiesAsync</c>: the package writes the
    /// consent cookie server-side precisely so its attributes are right, and a hand-forged cookie
    /// risks a shape the site rejects. If that happened the scan would silently measure the
    /// undecided state six times over and report a clean bill of health.
    /// </remarks>
    private async Task DecideAsync(IBrowserContext context, ConsentPass pass)
    {
        object? decision = DecisionFor(pass);

        if (decision is null)
        {
            return;
        }

        // Load the root first so the context has an origin the cookie can be stored against.
        IPage warmUp = await context.NewPageAsync();
        await warmUp.GotoAsync(options.Url.ToString(), new PageGotoOptions { Timeout = 30_000 });
        await warmUp.CloseAsync();

        string endpoint = new Uri(options.Url, endpointPath).ToString();

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
