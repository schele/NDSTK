using System.Runtime.InteropServices;

namespace NDSTK;

/// <summary>
/// Last-resort reporting for a failure that happens before Serilog exists.
/// </summary>
/// <remarks>
/// Umbraco configures Serilog inside CreateUmbracoBuilder, so everything before that - reading
/// appsettings.Secrets.json, binding configuration, resolving the container - dies with nothing
/// written anywhere at all. On IIS that surfaces as a bare "HTTP Error 500.30 - ASP.NET Core app
/// failed to start" beside an untouched log folder, which tells you only that the process is gone.
/// It cost a night to work out that the silence was the diagnosis.
///
/// Two channels, because they fail for different reasons. Standard error needs no file permissions
/// and no folder to exist: the ASP.NET Core Module captures it into stdoutLogFile, and puts it in
/// the browser itself when ASPNETCORE_DETAILEDERRORS is set. The file is for when nobody turned
/// either of those on before the site went down, which is how this is normally discovered.
///
/// What this cannot catch is a failure before managed code runs - a missing assembly, or the
/// app-local ICU natives under runtimes/win-x64/native. Those never reach Main. For those the
/// module's own debugLevel=FILE,TRACE log is the only witness, which is why the published
/// web.config now carries it.
/// </remarks>
internal static class StartupFailureLog
{
    private const string FileName = "startup-failure.log";

    /// <summary>
    /// Writes what is known about a fatal startup exception. Never throws: it is called from a
    /// catch block on the way to the process ending, and an exception raised here would replace
    /// the one worth reading.
    /// </summary>
    internal static void Report(Exception exception)
    {
        string report = Compose(exception);

        try
        {
            // First, because it is the channel that survives a read-only or misowned directory.
            Console.Error.WriteLine(report);
            Console.Error.Flush();
        }
        catch
        {
            // Nothing to fall back to, and nowhere to say so.
        }

        foreach (string directory in Destinations())
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, FileName), report);
                return;
            }
            catch
            {
                // Try the next one. A locked-down app pool identity is exactly the case this loop
                // exists for.
            }
        }
    }

    /// <summary>
    /// Beside the Umbraco logs first - that folder is already proven writable by the app pool
    /// identity, and it is where somebody looking for a log will look. Then the application
    /// directory, then the temp directory, which is writable when very little else is.
    /// </summary>
    private static IEnumerable<string> Destinations()
    {
        string root = AppContext.BaseDirectory;

        yield return Path.Combine(root, "umbraco", "Logs");
        yield return root;
        yield return Path.GetTempPath();
    }

    /// <summary>
    /// The environment is included deliberately. Half the startup failures worth having a log for
    /// are answered by the framework version or the content root alone, and neither is visible
    /// from a 500.30 page.
    /// </summary>
    private static string Compose(Exception exception)
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine();
        report.AppendLine("================================================================");
        report.AppendLine($"NDSTK failed to start at {DateTime.UtcNow:u}");
        report.AppendLine("================================================================");
        report.AppendLine($"Machine           {Environment.MachineName}");
        report.AppendLine($"Process           {Environment.ProcessId}");
        report.AppendLine($"Framework         {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"Architecture      {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"Base directory    {AppContext.BaseDirectory}");
        report.AppendLine($"Current directory {Environment.CurrentDirectory}");
        report.AppendLine($"Environment       {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(unset)"}");
        report.AppendLine();
        report.AppendLine(exception.ToString());
        report.AppendLine();

        return report.ToString();
    }
}
