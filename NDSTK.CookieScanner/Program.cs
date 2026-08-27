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

    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
