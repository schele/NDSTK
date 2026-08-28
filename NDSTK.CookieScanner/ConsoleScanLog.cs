namespace NDSTK.CookieScanner;

/// <summary>
/// The console tool's log: progress to stdout, problems to stderr.
/// </summary>
/// <remarks>
/// Writes the message verbatim, with no level prefix or timestamp. The engine passes complete
/// sentences and the pre-refactor build wrote exactly those strings, so decorating them here would
/// break the output comparison that gates this refactor - and would make a pipeline grepping the
/// output start missing lines.
/// </remarks>
public sealed class ConsoleScanLog : IScanLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warning(string message) => Console.Error.WriteLine(message);
}
