using NDSTK.CookieScanner;

try
{
    ScanOptions options = ScanOptions.Parse(args);
    var log = new ConsoleScanLog();

    ScanResult? result = await new ScanRunner(options, () => CatalogueSource.Load(log), log)
        .RunAsync(CancellationToken.None);

    if (result is null)
    {
        return ScanReportWriter.ExitError;
    }

    ScanReportWriter.WriteFiles(options, result);
    ScanHistory.Save(result);

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
