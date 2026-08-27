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
    /// <param name="publicUrls">
    /// The URL list already discovered by <c>Program</c>'s public crawl, reused here to find the
    /// login page rather than re-crawled: a second full BFS solely to pick one URL out would
    /// roughly double the page loads this tool puts on a live site for no discovery benefit.
    /// </param>
    public async Task<PassResult> RunAsync(IReadOnlyList<Uri> publicUrls)
    {
        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        IPage page = await context.NewPageAsync();

        PageCapture.RecordHosts(page, hosts, options.Url);

        // Accept everything, so a cookie found here is attributable to the login rather than to a
        // consent state this dimension did not mean to test.
        await ConsentDecision.RecordAsync(context, options.Url, endpointPath, ConsentPass.MemberArea);

        Uri? portal = await SignInAsync(page, publicUrls);

        if (portal is null)
        {
            Console.Error.WriteLine(
                "  Member login did not appear to succeed - skipping the member dimension. Check "
                + "the credentials, and that the account is activated.");

            return new PassResult(ConsentPass.MemberArea, [], hosts);
        }

        IReadOnlyList<Uri> memberUrls = await new SiteCrawler(page, options).DiscoverAsync(portal);

        Dictionary<(string Name, StorageKind Storage), PassEntry> found = [];

        foreach (Uri url in memberUrls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                // Keyed on the lowercased name so this dedup agrees with
                // ObservedEntries.EarliestPerName's case-insensitive grouping - the stored entry
                // still carries the original casing, only the key is normalised.
                found.TryAdd(
                    (entry.Name.ToLowerInvariant(), entry.Storage),
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
    private async Task<Uri?> SignInAsync(IPage page, IReadOnlyList<Uri> publicUrls)
    {
        // The login page is found rather than assumed: its URL is editor-owned content.
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
