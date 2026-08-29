namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The dashboard's log: every line the scan emits, posted into the page.
/// </summary>
/// <remarks>
/// Every write marshals to the UI thread. The scan runs on a background task and the engine logs from
/// Playwright's own threads, so posting directly would throw - and would do it on a failure path that
/// is rarely exercised, which is the worst place to discover it.
/// <para>
/// The marshalling lives in <see cref="DashboardBridge.Post"/>, so every sender gets it rather than
/// only this one.
/// </para>
/// </remarks>
public sealed class WebViewScanLog(DashboardBridge bridge) : IScanLog
{
    public void Info(string message) => bridge.Post(new { type = "log", level = "info", message });

    public void Warning(string message) => bridge.Post(new { type = "log", level = "warning", message });
}
