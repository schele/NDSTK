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
