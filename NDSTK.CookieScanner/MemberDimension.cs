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
public sealed class MemberDimension(IBrowser browser, ScanOptions options, string endpointPath, IScanLog log)
{
    // The member auth cookie, by either name. Umbraco 18 on ASP.NET Core Identity issues
    // .AspNetCore.Identity.Application; UMB_MEMBER is the older name and is kept so this still
    // works against a site that issues it. Judged by cookie rather than by the landing url
    // because the login controller reports failure through ModelState and returns the same
    // page, so a url check would read a rejected password as success.
    private static readonly string[] MemberAuthCookies =
        [".AspNetCore.Identity.Application", "UMB_MEMBER"];


    /// <param name="publicUrls">
    /// The URL list already discovered by <c>Program</c>'s public crawl, reused here to find the
    /// login page rather than re-crawled: a second full BFS solely to pick one URL out would
    /// roughly double the page loads this tool puts on a live site for no discovery benefit.
    /// </param>
    public async Task<PassResult> RunAsync(IReadOnlyList<Uri> publicUrls)
    {
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);

        // The member dimension is additive, sitting outside the six comparable passes (see the
        // class remarks). A FillAsync timeout on login markup that changed, or any other Playwright
        // fault here - even after the six comparable passes already completed cleanly - must not
        // cost the whole scan its report: ManagementApiClient's remarks promise a failure must not
        // lose the scan's findings, and Program's single try/catch would otherwise skip
        // ScanReportWriter.Write entirely. Degrade exactly the way a failed login already does
        // below: log it and hand back an empty pass instead of throwing.
        try
        {
            // IgnoreHTTPSErrors is loopback-only - see the comment in ScanRunner.cs's discovery
            // context. This context is the one that matters most: it submits the member's email
            // and password.
            await using IBrowserContext context = await browser.NewContextAsync(
                new BrowserNewContextOptions { IgnoreHTTPSErrors = options.Url.IsLoopback });

            IPage page = await context.NewPageAsync();

            PageCapture.RecordHosts(page, hosts, options.Url);

            // Accept everything, so a cookie found here is attributable to the login rather than to
            // a consent state this dimension did not mean to test.
            await ConsentDecision.RecordAsync(context, options.Url, endpointPath, ConsentPass.MemberArea);

            Uri? portal = await SignInAsync(page, publicUrls);

            if (portal is null)
            {
                log.Warning(
                    "  Member login did not appear to succeed - no member auth cookie appeared "
                    + $"({string.Join(" or ", MemberAuthCookies)}). Check the credentials, and that "
                    + "the account is approved and its email confirmed.");

                return new PassResult(ConsentPass.MemberArea, [], hosts);
            }

            IReadOnlyList<Uri> memberUrls = await new SiteCrawler(page, options, log).DiscoverAsync(portal);

            Dictionary<(string Name, StorageKind Storage), PassEntry> found = [];

            foreach (Uri url in memberUrls)
            {
                PageObservation observation = await PageCapture.VisitAsync(page, url, hosts, log);

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
        catch (Exception error)
        {
            log.Warning(
                $"  Member dimension failed and was skipped: {error.Message}");

            return new PassResult(ConsentPass.MemberArea, [], hosts);
        }
    }

    /// <summary>
    /// Submits the login form and returns the URL it landed on, or null when it did not sign in.
    /// </summary>
    /// <remarks>
    /// Success is judged by one of <see cref="MemberAuthCookies"/> existing, not by the landing
    /// URL: the site's login controller returns the same page on failure with a ModelState error,
    /// so a URL check would read a rejected password as a success and then crawl the public site
    /// again, reporting nothing new and no error.
    /// </remarks>
    private async Task<Uri?> SignInAsync(IPage page, IReadOnlyList<Uri> publicUrls)
    {
        // The login page is found rather than assumed: its URL is editor-owned content.
        Uri? loginUrl = publicUrls.FirstOrDefault(url =>
            url.AbsolutePath.Contains("logga-in", StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase));

        if (loginUrl is null)
        {
            log.Warning("  No login page found in the crawl.");
            return null;
        }

        await page.GotoAsync(loginUrl.ToString(), new PageGotoOptions { Timeout = 30_000 });

        // Name attributes, matching Views/Login.cshtml's inputs.
        await page.FillAsync("input[name='Email']", options.MemberEmail!);
        await page.FillAsync("input[name='Password']", options.MemberPassword!);
        await page.ClickAsync("button[type='submit'], input[type='submit']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        bool signedIn = (await page.Context.CookiesAsync())
            .Any(cookie => MemberAuthCookies.Any(name =>
                cookie.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        return signedIn ? new Uri(page.Url) : null;
    }
}
