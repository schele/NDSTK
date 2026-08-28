using NDSTK.CookieScanner;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// Appends the scan's commentary to a text box, colouring warnings.
/// </summary>
/// <remarks>
/// Every write marshals to the UI thread. The scan runs on a background task and the engine logs
/// from Playwright's own threads, so appending directly would throw an invalid-cross-thread
/// exception - and would do it on a failure path that is rarely exercised, which is the worst
/// place to discover it.
/// </remarks>
public sealed class TextBoxScanLog(RichTextBox target) : IScanLog
{
    /// <remarks>
    /// Passes null rather than <c>target.ForeColor</c>: the box's own colour is a control property
    /// like any other, so reading it here would be one more off-thread touch on the very call this
    /// class exists to marshal. It is resolved on the UI thread instead.
    /// </remarks>
    public void Info(string message) => Append(message, colour: null);

    public void Warning(string message) => Append(message, Color.Firebrick);

    private void Append(string message, Color? colour)
    {
        // Guarded before the marshal, not after: without a handle there is nothing to marshal onto
        // and BeginInvoke throws rather than queues. Of the three members read here only
        // InvokeRequired is documented as safe to call from another thread - IsHandleCreated and
        // IsDisposed are not, they are simply single-field reads - and none of them makes this
        // guard atomic with the call below, which is what the catch is for.
        if (target.IsHandleCreated is false || target.IsDisposed)
        {
            return;
        }

        if (target.InvokeRequired)
        {
            try
            {
                target.BeginInvoke(() => Append(message, colour));
            }
            catch (ObjectDisposedException)
            {
                // The window closed between the guard above and this call. A log line whose text
                // box no longer exists has nowhere to go, and is not worth failing a scan over.
            }

            return;
        }

        target.SelectionStart = target.TextLength;
        target.SelectionLength = 0;
        target.SelectionColor = colour ?? target.ForeColor;
        target.AppendText(message + Environment.NewLine);
        target.SelectionColor = target.ForeColor;
        target.ScrollToCaret();
    }
}
