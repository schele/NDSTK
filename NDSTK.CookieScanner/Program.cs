using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

try
{
    ScanOptions options = ScanOptions.Parse(args);
    var log = new ConsoleScanLog();

    ScanResult? result = await new ScanRunner(options, () => LoadCatalogue(log), log)
        .RunAsync(CancellationToken.None);

    if (result is null)
    {
        return ScanReportWriter.ExitError;
    }

    ScanReportWriter.WriteFiles(options, result);
    // TASK 5 RESTORES THIS: ScanHistory.Save(result);

    foreach (string line in ScanReportWriter.SummaryLines(options, result))
    {
        log.Info(line);
    }

    return result.ExitCode;
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
static CookieCatalogue LoadCatalogue(IScanLog log)
{
    string beside = Path.Combine(AppContext.BaseDirectory, "cookie-catalogue.json");

    if (File.Exists(beside))
    {
        log.Info($"Using the catalogue override at {beside}.");

        return CookieCatalogue.Parse(File.ReadAllText(beside));
    }

    return CookieCatalogue.Default();
}
