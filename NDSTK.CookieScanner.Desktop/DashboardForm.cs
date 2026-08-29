using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The dashboard's shell: a single WebView2 control filling the client area, rendering pages served
/// by <see cref="DashboardAssets"/> over <c>https://app.localhost/</c>.
/// </summary>
public sealed class DashboardForm : Form
{
    private readonly WebView2 webView = new()
    {
        Dock = DockStyle.Fill,
    };

    /// <remarks>
    /// Read once, at construction. The variable is fixed for the life of the process, and a page that
    /// re-read it would suggest it could be changed without restarting. Reported as a plain fact
    /// rather than as a fault: report-only is a supported mode.
    /// </remarks>
    private readonly bool secretIsSet =
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScanOptions.SecretVariable)) is false;

    /// <summary>The secret's companion: the client id the environment pairs with it.</summary>
    /// <remarks>
    /// Dashboard-only, and optional. The id is not a secret - it is the name the site registers the
    /// API user under - so it could live in a profile, and it still can; this is for the machine that
    /// has the secret set once and should not need the id typed into every profile as well. Read
    /// once, like the secret, and for the same reason. Sent to the page as a value, not a flag: the
    /// page fills the client-id box with it when a profile has none of its own.
    /// </remarks>
    private const string ClientIdVariable = "NDSTK_COOKIESCAN_CLIENT_ID";

    private readonly string clientIdDefault =
        Environment.GetEnvironmentVariable(ClientIdVariable)?.Trim() ?? "";

    /// <remarks>
    /// Loaded here rather than on <c>ready</c>: a settings file that cannot be read costs the window
    /// its remembered options, and finding that out while the page waits for its first message is
    /// worse than finding it out before the page exists.
    /// <para>
    /// One instance, shared with <see cref="ScanSession"/>: saving a site and running a scan both
    /// write a profile, so two instances would be two lists racing each other onto the same file.
    /// Both mutate it on the UI thread only - this class from the message loop, the session before
    /// it hands anything to a background task - so there is no lock around it and none is needed.
    /// </para>
    /// </remarks>
    private readonly DashboardSettings settings = DashboardSettings.Load();

    private DashboardBridge? bridge;

    private ScanSession? session;

    public DashboardForm()
    {
        Text = "NDSTK cookie scanner";

        // From the embedded copy rather than Icon.ExtractAssociatedIcon(Environment.ProcessPath): the
        // resource is always there and never touches the disk, and the extracted-bundle layout of a
        // single-file publish is exactly the place a path-based lookup goes wrong.
        using (Stream? icon = typeof(DashboardForm).Assembly.GetManifestResourceStream("app.ico"))
        {
            if (icon is not null)
            {
                Icon = new Icon(icon);
            }
        }

        // Every size goes through LogicalToDeviceUnits: raw pixels render at two-thirds size on a
        // 150% display.
        ClientSize = LogicalToDeviceUnits(new Size(1280, 860));
        MinimumSize = LogicalToDeviceUnits(new Size(1040, 700));
        StartPosition = FormStartPosition.CenterScreen;

        // Do not touch Source or any CoreWebView2 member here: the control has no CoreWebView2 until
        // it is initialised, and constructor-time property validation crashed the previous window
        // twice.
        Controls.Add(webView);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _ = InitializeWebViewAsync();
    }

    /// <summary>Tells a running scan to stop, on the way out.</summary>
    /// <remarks>
    /// The same path the Cancel button takes, on the same session, with the same guarantee that
    /// nothing is written. It matters because the scan owns a browser process that only its own
    /// cancellation path tears down: <c>Application.Run</c> returns the moment this form closes, so
    /// without this the process exits with the run still on a background thread and the engine's
    /// <c>await using</c> teardown never reached, leaving Chromium to be reaped by Playwright's
    /// driver noticing that its parent died.
    /// <para>
    /// Cancel, never refuse: a window that argued about closing would be answering a question nobody
    /// asked. Nothing is waited for either - a close that hung for the rest of a pass would look
    /// like the window had frozen.
    /// </para>
    /// </remarks>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        session?.Cancel();

        base.OnFormClosing(e);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    this,
                    "NDSTK cookie scanner needs the WebView2 Evergreen runtime, which is not installed on " +
                    "this machine. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and " +
                    "run this program again.",
                    "WebView2 runtime not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                Close();

                return;
            }

            // The default user data folder is created beside the exe, which fails outright in
            // Program Files or on a read-only share - exactly where a portable exe ends up.
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NDSTK.CookieScanner",
                "webview2");

            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());

            // CreateAsync can span an arbitrary amount of time, and the user can close the window
            // while it is in flight. IsDisposed/Disposing are safe to read on a disposed Form; touching
            // `this` or `webView` beyond this point without checking would run against already-disposed
            // objects.
            if (IsDisposed || Disposing)
            {
                return;
            }

            await webView.EnsureCoreWebView2Async(environment);

            // Same reasoning as above: EnsureCoreWebView2Async is another await the window can outlive.
            if (IsDisposed || Disposing)
            {
                return;
            }

            CoreWebView2 core = webView.CoreWebView2!;

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif

            // A made-up name under .local costs a ~2 second DNS resolution timeout on every
            // navigation; names under .localhost resolve in tens of milliseconds.
            core.AddWebResourceRequestedFilter(
                "https://app.localhost/*",
                CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);

            core.WebResourceRequested += OnWebResourceRequested;

            // Before the navigation, not after: the page announces itself the moment its module runs,
            // and a bridge subscribed a millisecond later would miss that message and every envelope
            // it releases.
            bridge = new DashboardBridge(webView);
            bridge.CommandReceived += OnCommandReceived;

            session = new ScanSession(bridge, settings);

            core.Navigate("https://app.localhost/index.html");
        }
        catch (Exception error)
        {
            // The awaits above can span an arbitrary amount of time; if the window was closed while
            // one was in flight, `this` and `webView` are already disposed - there is nothing left to
            // report to and nothing left to close, and calling MessageBox.Show(this, ...) with a
            // disposed owner would throw inside the handler meant to report failures.
            if (IsDisposed || Disposing)
            {
                return;
            }

            // A window that throws during initialisation otherwise leaves a process alive with
            // nothing on screen, which is how the previous window's first crash hid itself.
            MessageBox.Show(
                this,
                error.Message,
                "NDSTK cookie scanner failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
        }
    }

    /// <summary>Routes one message from the page to whatever answers it.</summary>
    /// <remarks>
    /// A switch rather than a dictionary of handlers: the later pages add message types here, and a
    /// missing arm should be a compiler-visible gap in one method rather than a registration someone
    /// forgot. Every command <see cref="DashboardCommand.Parse"/> can produce now has an arm; a type
    /// added later without one falls through silently, which is the reason they are gathered here
    /// rather than registered in the file that introduces them.
    /// </remarks>
    private void OnCommandReceived(DashboardCommand command)
    {
        switch (command)
        {
            case ReadyCommand:
                // The first thing the page hears: what it may not persist, and what it remembered
                // last time. Posted on ready rather than baked into index.html, because the page is
                // an embedded resource and the settings are not.
                //
                // The sites go out decrypted, passwords included. That is the point of storing them:
                // the page fills its own password field from a saved profile, and a run posts back
                // what the field holds. The envelope never leaves the process - WebView2 hands it to
                // a renderer inside this exe, over no socket and no origin anything else can reach.
                bridge?.Post(new
                {
                    type = "state",
                    running = false,
                    secretIsSet,
                    secretVariable = ScanOptions.SecretVariable,
                    clientIdDefault,
                    clientIdVariable = ClientIdVariable,
                    sites = settings.Sites,
                    selectedUrl = settings.SelectedUrl,
                    // Load's decrypt failures, carried on the one message that is guaranteed to
                    // arrive after the log panel exists. The page prints them as warnings; there is
                    // nothing for it to do about them beyond telling the operator which field to
                    // retype.
                    warnings = settings.Warnings,
                });

                break;

            case SaveSiteCommand save:
                SaveSite(save.Profile);

                break;

            case DeleteSiteCommand delete:
                DeleteSite(delete.Url);

                break;

            case ListHistoryCommand:
                PostHistory();

                break;

            case LoadScanCommand load:
                PostScan(load);

                break;

            case CompareCommand compare:
                PostDiff(compare);

                break;

            case RunCommand run:
                // Not awaited: this handler is on the message loop, and a scan takes the best part
                // of a minute. StartAsync throws nothing - every failure inside it becomes a warning
                // line and a running state the page can trust.
                _ = session?.StartAsync(run);

                break;

            case CancelCommand:
                session?.Cancel();

                break;
        }
    }

    /// <summary>Saves the run card's current values as the profile for the URL they name.</summary>
    /// <remarks>
    /// Written to disk and answered in the same breath, because the two can disagree: the profile
    /// stored is the trimmed one, and a page left showing the untrimmed text would then Delete
    /// something it thinks is selected and is not. The answer is the file's own view of the list, so
    /// the dropdown is always what was actually written.
    /// <para>
    /// The saved profile becomes the selected one. Anything else would leave the operator having
    /// just saved a site and looking at a dropdown that says "New site".
    /// </para>
    /// </remarks>
    private void SaveSite(SiteProfile profile)
    {
        // The page count goes through the same rule a run applies, so Save site and Run store the
        // same number for the same form: a blank field arrives as zero, and a profile holding a zero
        // would put one in the spinner at every later launch - see ScanSession.Pages. It is the one
        // thing normalised out here, because it is the only one whose rule belongs to the scanner
        // rather than to the file; Upsert trims the URL and the three credentials for both callers.
        settings.Upsert(profile with { MaxPages = ScanSession.Pages(profile.MaxPages) });
        settings.SelectedUrl = profile.Url.Trim();
        settings.Save();

        bridge?.Post(DashboardAnswer.Sites(settings));
    }

    /// <summary>Forgets one profile.</summary>
    /// <remarks>
    /// The selection goes to nothing rather than to the neighbouring profile: deleting a site is not
    /// a request to open a different one, and a form that silently refilled itself with someone
    /// else's credentials after a Delete would be the worst possible answer to that click.
    /// </remarks>
    private void DeleteSite(string url)
    {
        settings.Remove(url);
        settings.SelectedUrl = null;
        settings.Save();

        bridge?.Post(DashboardAnswer.Sites(settings));
    }

    /// <summary>Answers <c>listHistory</c> with every kept scan, newest first.</summary>
    /// <remarks>
    /// Read here rather than cached, because the folder is shared: the console tool writes into it
    /// too, so a scan run from a terminal while this window is open belongs in the answer.
    /// <para>
    /// An empty list rather than an exception for a folder that cannot be read. This runs on the
    /// message loop, where a throw takes the loop down and with it the running scan's log - and
    /// <see cref="ScanHistory.List"/> already treats a file it cannot parse as one to skip, so the
    /// only failures left here are the folder-wide ones. A page that hears an empty history draws
    /// "No scans yet", which is the truth about what could be read.
    /// </para>
    /// </remarks>
    private void PostHistory()
        => bridge?.Post(new { type = "history", entries = ListScans(ScanHistory.Default()) });

    /// <summary>Every kept scan, newest first, or nothing at all for a folder that will not open.</summary>
    /// <remarks>
    /// Shared by all three answers that touch the folder, so the folder-wide failure is handled in
    /// one place. See <see cref="PostHistory"/> for why it is an empty list rather than a throw.
    /// </remarks>
    private static IReadOnlyList<ScanHistoryEntry> ListScans(ScanHistory history)
    {
        try
        {
            return history.List();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The scan a path names, read through the list rather than opened directly.</summary>
    /// <remarks>
    /// <see cref="ScanHistory.Load"/> takes the <see cref="ScanHistoryEntry"/> it listed, not a bare
    /// path - so the path the page sent is matched back against a fresh listing rather than opened.
    /// That is not a workaround for the missing overload: it also means the window only ever opens a
    /// file it just told the page about, never whatever path a message happens to name.
    /// <para>
    /// Null for a path with no match in <paramref name="entries"/> - deleted or renamed since the
    /// page's last <c>listHistory</c> - and null again for a match <see cref="ScanHistory.Load"/>
    /// itself cannot parse. The two are indistinguishable to the caller on purpose: both mean the
    /// page asked about a file that is not there to be read.
    /// </para>
    /// </remarks>
    private static ScanResult? LoadResult(
        ScanHistory history, IReadOnlyList<ScanHistoryEntry> entries, string path)
    {
        ScanHistoryEntry? entry = entries.FirstOrDefault(candidate => candidate.Path == path);

        return entry is null ? null : history.Load(entry);
    }

    /// <summary>Answers <c>loadScan</c> with the parsed scan the page asked for, by path.</summary>
    /// <remarks>
    /// Both of the ways <see cref="LoadResult"/> can come back empty - a path the list no longer holds, and
    /// a file that will not parse - answer the same way: an inline <c>error</c>, never a silent
    /// nothing.
    /// <para>
    /// Both envelopes echo <c>command.Path</c> back. This runs on the message loop and the page can
    /// have moved its selection on before the answer arrives - unchecked the scan it asked about,
    /// selected a different one - and without a way to tell which request an answer belongs to the
    /// page would have no choice but to render whatever comes back regardless of what is still
    /// selected.
    /// </para>
    /// </remarks>
    private void PostScan(LoadScanCommand command)
    {
        ScanHistory history = ScanHistory.Default();

        ScanResult? result = LoadResult(history, ListScans(history), command.Path);

        if (result is null)
        {
            bridge?.Post(new
            {
                type = "error",
                path = command.Path,
                message = "That scan could not be loaded. It may have been deleted or its file damaged.",
            });

            return;
        }

        bridge?.Post(new { type = "scan", path = command.Path, result });
    }

    /// <summary>Answers <c>compare</c> with what changed between two kept scans.</summary>
    /// <remarks>
    /// The pair is ordered by <see cref="ScanResult.CompletedAt"/> and never by the order the page
    /// asked in, because "appeared" has to mean one thing: present in the newer scan and not in the
    /// older. Checking the two rows bottom-up would otherwise invert every group.
    /// <para>
    /// Both envelopes echo <c>paths</c> - the two the page asked about, in the order it asked, which
    /// is a different question from the ordering above. It is the same staleness guard
    /// <see cref="PostScan"/> needs, in the shape this answer can carry: two rows checked is exactly
    /// the state in which no single path is selected, so a bare <c>message</c> would arrive with
    /// nothing for the page to match it against and would be dropped by the guard it already has.
    /// </para>
    /// <para>
    /// The options summaries ride along on each side rather than being reduced here to a sentence.
    /// What differed is a fact about the two files; how to say it out loud is the page's, alongside
    /// every other piece of wording in this window.
    /// </para>
    /// </remarks>
    private void PostDiff(CompareCommand command)
    {
        ScanHistory history = ScanHistory.Default();

        IReadOnlyList<ScanHistoryEntry> entries = ListScans(history);

        string[] paths = [command.PathA, command.PathB];

        ScanResult? a = LoadResult(history, entries, command.PathA);
        ScanResult? b = LoadResult(history, entries, command.PathB);

        if (a is null || b is null)
        {
            bridge?.Post(new
            {
                type = "error",
                paths,
                message = "One of those scans could not be read.",
            });

            return;
        }

        (ScanResult older, ScanResult newer) = a.CompletedAt <= b.CompletedAt ? (a, b) : (b, a);

        ScanDiff diff = ScanDiff.Between(older.Candidates, newer.Candidates);

        // Both, not either: a pair is only known to have run the same way when both files say how
        // they ran. One recorded summary beside a null is "not recorded", which is the honest answer
        // for a history file written before the summary existed.
        bool optionsKnown = older.Options is not null && newer.Options is not null;

        bridge?.Post(new
        {
            type = "diff",
            paths,
            older = Side(older),
            newer = Side(newer),
            appeared = diff.Appeared,
            disappeared = diff.Disappeared,
            recategorised = diff.Recategorised,
            optionsKnown,
            // Record equality, so a field added to ScanOptionsSummary later is compared without
            // anything here having to be told about it.
            optionsDiffer = optionsKnown && older.Options != newer.Options,
            siteDiffers = SiteKey(older.Site) != SiteKey(newer.Site),
        });

        static object Side(ScanResult result) => new
        {
            result.CompletedAt,
            result.Site,
            entryCount = result.Candidates.Count,
            result.Options,
        };

        // The site is recorded as the scanned Uri's own text, so two runs of one site agree
        // exactly - but a hand-written history file, or a future front end that records what was
        // typed, would not. Trailing slash and case are the two ways one site spells itself
        // differently; anything past that really is a different site.
        static string SiteKey(string? site)
            => (site ?? string.Empty).TrimEnd('/').ToLowerInvariant();
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string path = new Uri(e.Request.Uri).AbsolutePath;

        if (DashboardAssets.TryOpen(path, out Stream content, out string contentType) is false)
        {
            e.Response = webView.CoreWebView2!.Environment.CreateWebResourceResponse(
                null, 404, "Not Found", "Content-Type: text/plain");

            return;
        }

        // No caching: the assets change only when the exe does, and a stale cache across an upgrade
        // would be a bug nobody could reproduce.
        e.Response = webView.CoreWebView2!.Environment.CreateWebResourceResponse(
            content, 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: no-store");
    }
}
