using Microsoft.Playwright;
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

// The package's default consent endpoint. Not a flag: a site that has moved it has also moved its
// own JavaScript, so a mismatch here would be the least of that site's problems.
const string ConsentEndpointPath = "/api/cookie-consent";

try
{
    ScanOptions options = ScanOptions.Parse(args);

    Console.WriteLine($"Scanning {options.Url} - up to {options.MaxPages} pages per pass, locale {options.Locale}.");

    BrowserBootstrap.EnsureChromium();

    CookieCatalogue catalogue = LoadCatalogue();

    using IPlaywright playwright = await Playwright.CreateAsync();

    await using IBrowser browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = options.Headed is false });

    IReadOnlyList<Uri> urls;

    // Discovery runs in its own throwaway context so the pages it loads cannot leave cookies in
    // any pass's jar.
    await using (IBrowserContext discovery = await browser.NewContextAsync(
        new BrowserNewContextOptions { IgnoreHTTPSErrors = true }))
    {
        urls = await new SiteCrawler(await discovery.NewPageAsync(), options).DiscoverAsync(options.Url);
    }

    if (urls.Count == 0)
    {
        Console.Error.WriteLine(
            $"Found no HTML pages at {options.Url}. Is the site running, and is the URL right?");

        return ScanReportWriter.ExitError;
    }

    Console.WriteLine($"Discovered {urls.Count} page(s). Running {ConsentPasses.Comparable.Count} passes.");

    var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath);
    List<ObservedEntry> observed = [];
    Dictionary<ConsentPass, IReadOnlySet<string>> hostsByPass = [];

    foreach (ConsentPass pass in ConsentPasses.Comparable)
    {
        Console.WriteLine($"  pass {(int)pass + 1}/{ConsentPasses.Comparable.Count}: {pass}");

        PassResult result = await runner.RunAsync(pass, urls);

        hostsByPass[pass] = result.Hosts;
        observed.AddRange(result.Entries.Select(entry => new ObservedEntry(
            entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires)));
    }

    if (options.MemberScanEnabled)
    {
        Console.WriteLine("  member dimension: signing in");

        PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath).RunAsync(urls);

        hostsByPass[ConsentPass.MemberArea] = member.Hosts;
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

    // From the RAW observations, not from candidates. A violation is a property of one sighting,
    // while a candidate is the earliest-per-name reduction - so deriving violations from the
    // reduced list would miss a cookie whose category WAS granted in the pass that first set it
    // and which was then set again in a pass that granted something else. That second sighting is
    // the signature of a tag respecting consent selectively, which is the thing the passes exist
    // to catch.
    IReadOnlyList<CookieDeclarationCandidate> violations =
        ViolationScan.Find(observed, catalogue, now, options.Locale);

    // Computed here rather than taken from the endpoint: it depends on THIS run's catalogue, which
    // may be an override file the site knows nothing about.
    IReadOnlyList<string> expectedButNotObserved =
        [.. MergePlanner.Plan(candidates, [], catalogue).ExpectedButNotObserved];

    MergeOutcome? outcome = null;

    // TASK 13 RESTORES THIS. ManagementApiClient does not exist yet; leaving the call commented
    // out keeps the scanner runnable and verifiable through Tasks 11 and 12.
    // if (options.CanReachApi)
    // {
    //     outcome = await new ManagementApiClient(options).MergeAsync(candidates);
    // }

    return ScanReportWriter.Write(
        options, candidates, violations, expectedButNotObserved, hostsByPass, outcome);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);

    return ScanReportWriter.ExitError;
}
catch (Exception error)
{
    Console.Error.WriteLine($"The scan failed: {error.Message}");

    return ScanReportWriter.ExitError;
}

// An override beside the exe replaces the embedded catalogue wholesale, so legal wording can be
// changed without a rebuild.
static CookieCatalogue LoadCatalogue()
{
    string beside = Path.Combine(AppContext.BaseDirectory, "cookie-catalogue.json");

    if (File.Exists(beside))
    {
        Console.WriteLine($"Using the catalogue override at {beside}.");

        return CookieCatalogue.Parse(File.ReadAllText(beside));
    }

    return CookieCatalogue.Default();
}
