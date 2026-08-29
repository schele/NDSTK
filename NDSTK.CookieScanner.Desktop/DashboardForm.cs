using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

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

    /// <remarks>
    /// Loaded here rather than on <c>ready</c>: a settings file that cannot be read costs the window
    /// its remembered options, and finding that out while the page waits for its first message is
    /// worse than finding it out before the page exists.
    /// </remarks>
    private readonly DashboardSettings settings = DashboardSettings.Load();

    private DashboardBridge? bridge;

    private ScanSession? session;

    public DashboardForm()
    {
        Text = "NDSTK cookie scanner";

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

            session = new ScanSession(bridge);

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
    /// forgot. The types this build does not answer yet fall through deliberately - the page they
    /// belong to does not exist, so nothing can send them.
    /// </remarks>
    private void OnCommandReceived(DashboardCommand command)
    {
        switch (command)
        {
            case ReadyCommand:
                // The first thing the page hears: what it may not persist, and what it remembered
                // last time. Posted on ready rather than baked into index.html, because the page is
                // an embedded resource and the settings are not.
                bridge?.Post(new
                {
                    type = "state",
                    running = false,
                    secretIsSet,
                    secretVariable = ScanOptions.SecretVariable,
                    settings,
                });

                break;

            case ListHistoryCommand:
                PostHistory();

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
    {
        IReadOnlyList<ScanHistoryEntry> entries;

        try
        {
            entries = ScanHistory.Default().List();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            entries = [];
        }

        bridge?.Post(new { type = "history", entries });
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
