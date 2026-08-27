using Microsoft.Playwright;
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

// Replaced in Task 10 with the real orchestration. For now it proves the CLI parses and Chromium
// can be provisioned, which are the two things every later task depends on.
try
{
    ScanOptions options = ScanOptions.Parse(args);

    Console.WriteLine($"Would scan {options.Url} (max {options.MaxPages} pages, locale {options.Locale}).");
    Console.WriteLine($"Write-back: {(options.WriteBackEnabled ? "enabled" : "disabled")}.");
    Console.WriteLine($"Member scan: {(options.MemberScanEnabled ? "enabled" : "disabled")}.");

    BrowserBootstrap.EnsureChromium();

    Console.WriteLine("Chromium is ready.");

    using IPlaywright playwright = await Microsoft.Playwright.Playwright.CreateAsync();

    await using IBrowser browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = options.Headed is false });

    // IgnoreHTTPSErrors so a scan of a local site behind a dev certificate works without the
    // operator having to trust it first.
    await using IBrowserContext context = await browser.NewContextAsync(
        new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

    IPage page = await context.NewPageAsync();

    IReadOnlyList<Uri> urls = await new SiteCrawler(page, options).DiscoverAsync(options.Url);

    Console.WriteLine($"Discovered {urls.Count} page(s):");

    foreach (Uri url in urls)
    {
        Console.WriteLine($"  {url}");
    }

    // The endpoint path the site actually uses. The package default; override with --endpoint-path
    // is deliberately not offered, because a site that has moved it has also moved its own JS.
    const string ConsentEndpointPath = "/api/cookie-consent";

    var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath);
    List<ObservedEntry> observed = [];
    Dictionary<ConsentPass, IReadOnlySet<string>> hostsByPass = [];

    foreach (ConsentPass pass in ConsentPasses.Comparable)
    {
        Console.WriteLine($"Pass {(int)pass + 1}/6: {pass}...");

        PassResult result = await runner.RunAsync(pass, urls);

        hostsByPass[pass] = result.Hosts;

        foreach (PassEntry entry in result.Entries)
        {
            observed.Add(new ObservedEntry(
                entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires));
        }

        Console.WriteLine($"  {result.Entries.Count} entr(ies), {result.Hosts.Count} third-party host(s)");
    }

    if (options.MemberScanEnabled)
    {
        Console.WriteLine("Member dimension: signing in...");

        PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath).RunAsync();

        hostsByPass[ConsentPass.MemberArea] = member.Hosts;

        foreach (PassEntry entry in member.Entries)
        {
            observed.Add(new ObservedEntry(
                entry.Name, entry.Storage, ConsentPass.MemberArea,
                entry.FirstUrl.ToString(), entry.Expires));
        }

        Console.WriteLine($"  {member.Entries.Count} entr(ies) in the member area");
    }

    IReadOnlyList<ObservedEntry> earliest = ObservedEntries.EarliestPerName(observed);

    Console.WriteLine($"\n{earliest.Count} distinct entr(ies) across all passes:");

    foreach (ObservedEntry entry in earliest)
    {
        Console.WriteLine($"  {entry.Name} [{entry.Storage}] first seen in {entry.FirstSeenPass}");
    }

    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
