using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// The signed-in dimension: log in, then discover and visit the member area.
/// </summary>
/// <remarks>
/// Its own discovery rather than a replay of the public URL list, because the pages of interest -
/// the portal, bookings, children - are only linked once signed in. That is also why this sits
/// outside the six comparable passes: it visits a different URL set, so its findings cannot be
/// compared by pass order against them.
/// <para>
/// Login is the only form this submits. Nothing here POSTs a booking, a cancellation or a
/// payment: the scanner must not be able to create real records on a live site, which is why the
/// TempData cookie stays a documented limitation rather than something to chase.
/// </para>
/// </remarks>
public sealed class MemberDimension(IBrowser browser, ScanOptions options, string endpointPath)
{
    public async Task<PassResult> RunAsync()
    {
        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        IPage page = await context.NewPageAsync();

        PageCapture.RecordHosts(page, hosts, options.Url);

        // Accept everything, so a cookie found here is attributable to the login rather than to a
        // consent state this dimension did not mean to test.
        IAPIResponse decision = await context.APIRequest.PostAsync(
            new Uri(options.Url, endpointPath).ToString(),
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    action = "accept-all",
                    categories = new[] { "preferences", "statistics", "marketing" },
                },
            });

        if (decision.Ok is false)
        {
            throw new InvalidOperationException(
                $"Could not record consent for the member dimension (HTTP {decision.Status}).");
        }

        Uri? portal = await SignInAsync(page);

        if (portal is null)
        {
            Console.Error.WriteLine(
                "  Member login did not appear to succeed - skipping the member dimension. Check "
                + "the credentials, and that the account is activated.");

            return new PassResult(ConsentPass.MemberArea, [], hosts);
        }

        IReadOnlyList<Uri> memberUrls = await new SiteCrawler(page, options).DiscoverAsync(portal);

        Dictionary<(string, StorageKind), PassEntry> found = [];

        foreach (Uri url in memberUrls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                found.TryAdd(
                    (entry.Name, entry.Storage),
                    new PassEntry(entry.Name, entry.Storage, entry.Expires, url));
            }
        }

        return new PassResult(ConsentPass.MemberArea, [.. found.Values], hosts);
    }

    /// <summary>
    /// Submits the login form and returns the URL it landed on, or null when it did not sign in.
    /// </summary>
    /// <remarks>
    /// Success is judged by the UMB_MEMBER cookie existing, not by the landing URL: the site's
    /// login controller returns the same page on failure with a ModelState error, so a URL check
    /// would read a rejected password as a success and then crawl the public site again, reporting
    /// nothing new and no error.
    /// </remarks>
    private async Task<Uri?> SignInAsync(IPage page)
    {
        // The login page is found rather than assumed: its URL is editor-owned content.
        IReadOnlyList<Uri> publicUrls = await new SiteCrawler(page, options).DiscoverAsync(options.Url);

        Uri? loginUrl = publicUrls.FirstOrDefault(url =>
            url.AbsolutePath.Contains("logga-in", StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase));

        if (loginUrl is null)
        {
            Console.Error.WriteLine("  No login page found in the crawl.");
            return null;
        }

        await page.GotoAsync(loginUrl.ToString(), new PageGotoOptions { Timeout = 30_000 });

        // Name attributes, matching Views/Login.cshtml's inputs.
        await page.FillAsync("input[name='Email']", options.MemberEmail!);
        await page.FillAsync("input[name='Password']", options.MemberPassword!);
        await page.ClickAsync("button[type='submit'], input[type='submit']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        bool signedIn = (await page.Context.CookiesAsync())
            .Any(cookie => cookie.Name.Equals("UMB_MEMBER", StringComparison.OrdinalIgnoreCase));

        return signedIn ? new Uri(page.Url) : null;
    }
}
