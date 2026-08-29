using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// One scan: the options it runs with, the task it runs on, and the files it leaves behind.
/// </summary>
/// <remarks>
/// Owns a run and nothing else. It knows the page only through <see cref="DashboardBridge"/>, so the
/// same session drives the log, the result and the running state without ever touching a control.
/// <para>
/// The one thing it owns beyond the run is a share of <paramref name="settings"/> - the same
/// instance <see cref="DashboardForm"/> holds, not a copy. A run saves the profile it ran with, and
/// two instances would be two lists overwriting each other's file. Everything this class does to it
/// happens on the UI thread, before the scan reaches <c>Task.Run</c>; see the remark on
/// <see cref="DashboardSettings"/> for what that buys and what would break it.
/// </para>
/// </remarks>
public sealed class ScanSession(DashboardBridge bridge, DashboardSettings settings)
{
    private readonly WebViewScanLog log = new(bridge);

    private CancellationTokenSource? cancellation;

    /// <summary>
    /// Where the window writes its report files.
    /// </summary>
    /// <remarks>
    /// Not the current directory, which is what the console tool defaults to. A window's current
    /// directory is wherever it happened to be launched from - a desktop shortcut leaves it at the
    /// system directory - so reports would scatter or fail to write. The last two lines of
    /// <see cref="ScanReportWriter.SummaryLines"/> name both files and reach the page on the result
    /// envelope, so the operator is still told where they went.
    /// </remarks>
    private static string ReportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "reports");

    /// <summary>Runs one scan, reporting everything it does through the bridge.</summary>
    /// <remarks>
    /// Nothing is thrown out of here. The caller is a message handler with no user to apologise to,
    /// so a failure is a warning line in the page's log and a running state the page can trust.
    /// </remarks>
    public async Task StartAsync(RunCommand command)
    {
        if (cancellation is not null)
        {
            log.Warning("A scan is already running.");

            return;
        }

        ScanOptions options;

        try
        {
            options = BuildOptions(command);
        }
        catch (ArgumentException error)
        {
            // Nothing started, so the page is put back to idle rather than left waiting for a run
            // that never began.
            log.Warning(error.Message);
            bridge.Post(new { type = "state", running = false });

            return;
        }

        // Remembered as soon as the options are known good, rather than only when the scan works. A
        // scan that fails is exactly when the operator has typed something worth not losing - a URL
        // that turned out to resolve to nothing, a client id being tried for the first time - and
        // that was the run that used to discard it. Not before the check above, though: a URL this
        // window has just refused is not one to hand back at every later launch, and is certainly
        // not one to add to the dropdown.
        //
        // This is the same upsert the Save site button performs, from the same values, so running a
        // scan against a URL with no profile yet creates one: "remember what was typed" and "save
        // this site" were always the same act, and now they are the same code. The client secret is
        // still not among the values - see DashboardSettings.
        settings.Upsert(Remembered(command));
        settings.SelectedUrl = command.Url.Trim();
        settings.Save();

        // Answered as well as written, so the dropdown is never a relaunch behind the file. Without
        // this, a scan of a new URL would save a profile the operator cannot see, select, or delete
        // until the window is restarted.
        bridge.Post(DashboardAnswer.Sites(settings));

        bridge.Post(new { type = "state", running = true });

        cancellation = new CancellationTokenSource();

        // Copied out of the field: the field is nullable and reassigned per run, and the lambda
        // below outlives the statement that assigned it.
        CancellationToken token = cancellation.Token;

        try
        {
            // Task.Run so Playwright's synchronous startup cannot block the UI thread.
            ScanResult? result = await Task.Run(
                () => new ScanRunner(options, () => CatalogueSource.Load(log), log).RunAsync(token),
                token);

            if (result is null)
            {
                log.Warning("The scan found no pages, so there is nothing to report.");

                return;
            }

            // Posted before anything is written, and carrying the summary lines with it. A report
            // file left open in an editor, or a full disk, used to throw straight past this and cost
            // the operator the findings of a scan that had actually succeeded. The summary lines name
            // the paths the writes below are about to create, so the counts reach the page even when
            // one of those writes then fails.
            bridge.Post(new
            {
                type = "result",
                scan = result,
                summary = ScanReportWriter.SummaryLines(options, result),
            });

            // Two blocks, not one: a locked report file must not cost the history entry - history is
            // written "in addition to" the report directory rather than after it - and neither may
            // cost the result the page already has. Narrow on purpose, unlike the settings write:
            // these files are the point of the exercise, so anything other than the disk refusing
            // them should still reach the handler below.
            try
            {
                ScanReportWriter.WriteFiles(options, result);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Warning($"The scan finished, but its report could not be written: {error.Message}");
            }

            try
            {
                ScanHistory.Save(result);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Warning($"The scan finished, but its history entry could not be written: {error.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            // Before the general handler, which would otherwise report a cancel as a failure. A
            // cancelled scan writes no report and produces no result: a partial scan presented as a
            // complete one would be worse than no scan at all.
            log.Warning("Cancelled. No report was written.");
        }
        catch (Exception error)
        {
            log.Warning($"The scan failed: {error.Message}");
        }
        finally
        {
            bridge.Post(new { type = "state", running = false });

            // Disposed here and nowhere else: everything outside the awaited task runs on the UI
            // thread, so no cancel message can interleave and reach a disposed source. Nulled so the
            // next run cannot be handed the spent one.
            cancellation.Dispose();
            cancellation = null;
        }
    }

    /// <remarks>
    /// Says so in the log, because the engine only observes a cancel between passes: without a line
    /// here the window looks like it ignored the click for the rest of the current pass.
    /// </remarks>
    public void Cancel()
    {
        if (cancellation is null)
        {
            return;
        }

        log.Info("Cancelling - the scan stops at the end of the pass it is running.");

        cancellation.Cancel();
    }

    /// <summary>
    /// Turns one message from the page into the same <see cref="ScanOptions"/> the command line
    /// would have built.
    /// </summary>
    /// <remarks>
    /// The URL is checked with <see cref="Uri.TryCreate"/> against <see cref="UriKind.Absolute"/>,
    /// which is the rule <see cref="ScanOptions.Parse"/> applies, and the message names the same
    /// likely cause - a URL pasted without its scheme. Only the wording differs, because a window
    /// telling someone to fix a flag it does not have would be nonsense. Two front ends accepting
    /// different URLs is a bug nobody could reproduce from the other one.
    /// </remarks>
    /// <exception cref="ArgumentException">The site URL is not absolute.</exception>
    private static ScanOptions BuildOptions(RunCommand command)
    {
        string url = command.Url.Trim();

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? root) is false)
        {
            throw new ArgumentException(
                $"'{url}' is not an absolute URL. It needs a scheme, for example https://ndstk.se");
        }

        return new ScanOptions(
            Url: root,
            // The policy page lives on the site being scanned. --target exists for the case where it
            // does not, which is a console-tool concern: nothing in the window offers it, so the root
            // here is the deliberate answer rather than an omission.
            Target: root,
            MaxPages: Pages(command.MaxPages),
            Locale: ParseLocale(command.Locale),
            MemberEmail: Supplied(command.MemberEmail),
            MemberPassword: Supplied(command.MemberPassword),
            ClientId: Supplied(command.ClientId),
            // From the environment, never from a field: the same reason the console tool refuses a
            // --client-secret flag.
            ClientSecret: Environment.GetEnvironmentVariable(ScanOptions.SecretVariable),
            DryRun: command.DryRun,
            ReportDir: ReportDirectory,
            // Headless, always. --headed exists to debug the engine; a window that opened a second
            // visible browser on every run would be answering a question nobody asked.
            Headed: false);

        // Blank means absent, matching the console tool, where a flag that was not passed is null.
        static string? Supplied(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// The console tool's rule for a page count it cannot use: anything not positive means the
    /// default rather than a crawl of nothing.
    /// </summary>
    /// <remarks>
    /// One rule, read by both the options and the settings, because they must agree. A blank field
    /// reaches the host as zero by design; remembering that zero rather than the 25 that actually
    /// ran would put a 0 in the spinner at every later launch - a value the form's own min="1"
    /// cannot correct, since the settings are assigned rather than typed.
    /// <para>
    /// Public for the third caller: Save site writes a profile without running anything, from the
    /// same form and so with the same zero. Reached across from <see cref="DashboardForm"/> rather
    /// than copied there, because a second 25 would be a second rule the moment either moved.
    /// </para>
    /// </remarks>
    public static int Pages(int requested) => requested > 0 ? requested : 25;

    /// <summary>The run, as the profile for the site it ran against.</summary>
    /// <remarks>
    /// Normalised the same way <see cref="BuildOptions"/> normalises them - trimmed, and the page
    /// count put through <see cref="Pages"/> - so the profile holds what actually ran rather than
    /// what was typed. A blank max-pages field reaches the host as zero and the scan runs 25; a
    /// profile that stored the zero would put it back in the spinner at every later launch, which
    /// <see cref="Pages"/>'s own remark is about.
    /// <para>
    /// The password is trimmed for the same reason and no other: <see cref="BuildOptions"/> already
    /// trims the one it signs in with, so storing the untrimmed text would save a value the scan
    /// never used.
    /// </para>
    /// </remarks>
    private static SiteProfile Remembered(RunCommand command) => new(
        Url: command.Url.Trim(),
        MaxPages: Pages(command.MaxPages),
        Locale: ParseLocale(command.Locale),
        DryRun: command.DryRun,
        MemberEmail: command.MemberEmail?.Trim() ?? "",
        MemberPassword: command.MemberPassword?.Trim() ?? "",
        ClientId: command.ClientId?.Trim() ?? "");

    /// <remarks>
    /// Swedish for anything unrecognised, which is the rule the console tool applies to --locale.
    /// The page sends the enum's name, and a name this build does not know is a page out of step
    /// with its host rather than a reason to refuse the run.
    /// </remarks>
    private static Locale ParseLocale(string locale)
        => Enum.TryParse(locale, ignoreCase: true, out Locale parsed) ? parsed : Locale.Sv;
}
