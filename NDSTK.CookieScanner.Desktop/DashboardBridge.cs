using System.Text.Json;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The one channel between the window and the page: envelopes out, commands in.
/// </summary>
/// <remarks>
/// Web messages rather than <c>AddHostObjectToScript</c>: no COM ceremony, and the pages added later
/// introduce message types rather than transports. This class is deliberately the whole transport -
/// nothing else in the exe touches <see cref="CoreWebView2.PostWebMessageAsJson"/>.
/// </remarks>
public sealed class DashboardBridge
{
    /// <summary>
    /// How many envelopes are held while the page loads. Oldest first out when it is reached.
    /// </summary>
    /// <remarks>
    /// Bounded because the queue is only ever drained by a page that arrives, and a page that never
    /// arrives - a navigation that failed, a renderer that died - would otherwise let a running scan
    /// grow it without limit. Five hundred lines is more than a scan's whole commentary.
    /// </remarks>
    private const int Backlog = 500;

    private readonly WebView2 webView;

    // Both touched on the UI thread and nowhere else: Post marshals before it reaches Send, and
    // WebMessageReceived already arrives on the UI thread. That confinement is what lets this class
    // hold a queue and a flag without a lock around either. The one gap: if DashboardForm's handle
    // is destroyed between the IsHandleCreated read and InvokeRequired below, InvokeRequired can
    // itself come back false and Send runs unmarshalled on whatever thread called Post - Playwright's,
    // mid-scan. Tolerated rather than locked, because the worst that follows is a queue mutation on a
    // window that is already gone, and Deliver's own catch absorbs whatever comes after that.
    private readonly Queue<string> pending = new();

    private bool ready;

    /// <summary>Raised on the UI thread for every message the page sends that parses.</summary>
    public event Action<DashboardCommand>? CommandReceived;

    /// <param name="webView">An initialised control - <c>CoreWebView2</c> must already exist.</param>
    public DashboardBridge(WebView2 webView)
    {
        this.webView = webView;

        webView.CoreWebView2!.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Posts one envelope to the page, from any thread.</summary>
    /// <remarks>
    /// Serialised here, on the calling thread, before anything is marshalled: the envelope is
    /// usually built from a scan running on a background task, and turning it into a string while
    /// still on that thread keeps the UI thread's share of the work to one call.
    /// </remarks>
    public void Post(object envelope)
    {
        string json = JsonSerializer.Serialize(envelope, ScanJson.Options);

        // Guarded before the marshal, not after: without a handle there is nothing to marshal onto
        // and BeginInvoke throws rather than queues. Of the three members read here only
        // InvokeRequired is documented as safe to call from another thread - IsHandleCreated and
        // IsDisposed are not, they are simply single-field reads - and none of them makes this guard
        // atomic with the call below, which is what the catch is for.
        if (webView.IsHandleCreated is false || webView.IsDisposed)
        {
            return;
        }

        if (webView.InvokeRequired)
        {
            try
            {
                webView.BeginInvoke(() => Send(json));
            }
            catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException)
            {
                // The window closed between the guard above and this call. InvalidOperationException
                // is the likelier of the two, not the exotic one: a destroyed handle makes
                // Control.MarshaledInvoke throw "Invoke or BeginInvoke cannot be called on a control
                // until the window handle has been created", and only a control disposed at just the
                // right moment gives ObjectDisposedException. Either way a log line whose window no
                // longer exists has nowhere to go - and this runs on Playwright's threads, so an
                // escape here would unwind the engine mid-scan over a message nobody can read.
            }

            return;
        }

        Send(json);
    }

    /// <remarks>
    /// A message posted before the page has loaded is silently dropped by WebView2 - there is no
    /// error and no return value to check - which is how a scan started the instant the window
    /// opened used to lose its opening lines. So nothing is delivered until the page says
    /// <c>ready</c>, and everything before that waits in order.
    /// </remarks>
    private void Send(string json)
    {
        if (ready)
        {
            Deliver(json);

            return;
        }

        if (pending.Count == Backlog)
        {
            pending.Dequeue();
        }

        pending.Enqueue(json);
    }

    private void Deliver(string json)
    {
        try
        {
            webView.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch (ObjectDisposedException)
        {
            // The window closed between the marshal and this call - see the guard in Post.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        DashboardCommand? command = DashboardCommand.Parse(e.WebMessageAsJson);

        if (command is null)
        {
            return;
        }

        // Drained before the event is raised, so the backlog reaches the page ahead of whatever the
        // form posts in answer to ready. Re-entrant by design: a reload would say ready again, and
        // an empty queue makes that a no-op rather than a special case.
        if (command is ReadyCommand)
        {
            ready = true;

            while (pending.Count > 0)
            {
                Deliver(pending.Dequeue());
            }
        }

        CommandReceived?.Invoke(command);
    }
}
