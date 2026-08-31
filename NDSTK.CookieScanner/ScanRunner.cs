using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Runs one scan and returns what it found. Drives both front ends, so neither can drift from
/// what the other does - the console tool's exit code is what gates CI, and a window showing
/// different findings than CI acts on would be worse than no window.
/// </summary>
/// <param name="loadCatalogue">
/// A factory rather than an already-loaded value: the catalogue is loaded inside
/// <see cref="RunAsync"/>, right after the scan header has printed and Chromium has been checked,
/// so a malformed override file's diagnostics land in the run's own log, in the run's own order,
/// for both front ends - not before anything identifying the scan's target has been printed.
/// </param>
public sealed class ScanRunner(ScanOptions options, Func<CookieCatalogue> loadCatalogue, IScanLog log)
{
    // The package's default consent endpoint. Not a flag: a site that has moved it has also moved
    // its own JavaScript, so a mismatch here would be the least of that site's problems.
    private const string ConsentEndpointPath = "/api/cookie-consent";

    /// <summary>
    /// Returns null when discovery found no pages - there is nothing to report, and reporting an
    /// empty scan as a successful one would be a lie about coverage.
    /// </summary>
    public async Task<ScanResult?> RunAsync(CancellationToken cancellationToken)
    {
        // Upper-cased rather than printed as the enum's name: the dashboard's own Locale control
        // reads SV and EN, and a log line saying "Sv" beside it looks like a different value.
        log.Info(
            $"Scanning {options.Url} - up to {options.MaxPages} pages per pass, "
            + $"locale {options.Locale.ToString().ToUpperInvariant()}.");

        BrowserBootstrap.EnsureChromium(log);

        CookieCatalogue catalogue = loadCatalogue();

        using IPlaywright playwright = await Playwright.CreateAsync();

        await using IBrowser browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = options.Headed is false });

        IReadOnlyList<Uri> urls;

        // Discovery runs in its own throwaway context so the pages it loads cannot leave cookies
        // in any pass's jar.
        //
        // IgnoreHTTPSErrors is scoped to a loopback target, same as ManagementApiClient.CreateClient:
        // it exists so a local site behind a dev certificate can be scanned without trusting that
        // certificate first, and is deliberately not extended to a real host. MemberDimension submits
        // a member's email and password through one of these contexts, so accepting any certificate
        // when talking to production would be indefensible.
        await using (IBrowserContext discovery = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = options.Url.IsLoopback }))
        {
            urls = await new SiteCrawler(await discovery.NewPageAsync(), options, log)
                .DiscoverAsync(options.Url);
        }

        if (urls.Count == 0)
        {
            log.Warning($"Found no HTML pages at {options.Url}. Is the site running, and is the URL right?");

            return null;
        }

        // Discovery is the longest single phase, so a cancel signalled during it must not go
        // unobserved until whatever runs next decides to check for itself.
        cancellationToken.ThrowIfCancellationRequested();

        log.Info($"Discovered {urls.Count} page(s). Running {ConsentPasses.Comparable.Count} passes.");

        var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath, log);
        List<ObservedEntry> observed = [];
        Dictionary<ConsentPass, IReadOnlyList<string>> hostsByPass = [];

        foreach (ConsentPass pass in ConsentPasses.Comparable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            log.Info($"  pass {(int)pass + 1}/{ConsentPasses.Comparable.Count}: {pass}");

            PassResult result = await runner.RunAsync(pass, urls);

            // Sorted on the way in: System.Text.Json can serialize IReadOnlySet<string> but not
            // deserialize it, and scan history exists to read these files back. Sorting here also
            // keeps the serialized form deterministic and matches what the report itself writes.
            hostsByPass[pass] = [.. result.Hosts.Order()];
            observed.AddRange(result.Entries.Select(entry => new ObservedEntry(
                entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires)));
        }

        if (options.MemberScanEnabled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            log.Info("  member dimension: signing in");

            PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath, log)
                .RunAsync(urls);

            hostsByPass[ConsentPass.MemberArea] = [.. member.Hosts.Order()];
            observed.AddRange(member.Entries.Select(entry => new ObservedEntry(
                entry.Name, entry.Storage, ConsentPass.MemberArea,
                entry.FirstUrl.ToString(), entry.Expires)));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<CookieDeclarationCandidate> candidates = ObservedEntries
            .EarliestPerName(observed)
            .Select(entry => CategoryInference.Classify(entry, catalogue, now, options.Locale))
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.FirstSeenPass).First())
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

        // From the RAW observations, not from candidates. A violation is a property of one
        // sighting, while a candidate is the earliest-per-name reduction - so deriving violations
        // from the reduced list would miss a cookie whose category WAS granted in the pass that
        // first set it and which was then set again in a pass that granted something else. That
        // second sighting is the signature of a tag respecting consent selectively, which is the
        // thing the passes exist to catch.
        IReadOnlyList<CookieDeclarationCandidate> violations =
            ViolationScan.Find(observed, catalogue, now, options.Locale);

        // Computed here rather than taken from the endpoint: it depends on THIS run's catalogue,
        // which may be an override file the site knows nothing about.
        IReadOnlyList<string> expectedButNotObserved =
            [.. MergePlanner.Plan(candidates, [], catalogue).ExpectedButNotObserved];

        MergeOutcome? outcome = null;

        // A cancelled scan writes no report and produces no result - so it must not have already
        // posted to the management API and possibly saved a draft to the policy page. That is a
        // persistent side effect a discarded result cannot undo, which is worse than the write-back
        // simply not happening.
        cancellationToken.ThrowIfCancellationRequested();

        // An empty candidate list is a legitimate scan outcome, not a failure - but the endpoint's
        // own validation rejects a declarations-less request with a 400 ("The request contains no
        // declarations"), which would otherwise surface to the operator as a write-back failure for
        // a site that simply set no cookies.
        if (options.CanReachApi && candidates.Count > 0)
        {
            outcome = await new ManagementApiClient(options, log).MergeAsync(candidates);
        }

        return new ScanResult(
            candidates, violations, expectedButNotObserved, hostsByPass, outcome,
            options.CanReachApi, options.DryRun, now, options.Url.ToString(),
            new ScanOptionsSummary(options.MaxPages, options.Locale, options.MemberScanEnabled, options.DryRun));
    }
}
