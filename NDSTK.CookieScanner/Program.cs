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

    // Each write is guarded on its own, and neither is allowed to stop the other: history and the
    // report directory are independent per the spec ("in addition to"), not a sequence where a
    // failure of the first costs the second. Narrow on purpose - a full disk or a locked-down
    // profile, not a coding error - so anything else still reaches the catch below.
    try
    {
        ScanReportWriter.WriteFiles(options, result);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        log.Warning($"The report could not be written: {error.Message}");
    }

    try
    {
        ScanHistory.Save(result);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        log.Warning($"The scan history could not be written: {error.Message}");
    }

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
