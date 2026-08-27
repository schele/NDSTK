using Microsoft.Playwright;
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

    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
