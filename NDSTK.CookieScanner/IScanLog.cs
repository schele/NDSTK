namespace NDSTK.CookieScanner;

/// <summary>Where the scan's running commentary goes.</summary>
/// <remarks>
/// Injected rather than written straight to the console, because the same scan drives a console
/// tool and a window. Two levels only: <see cref="Info"/> is progress a reader expects, and
/// <see cref="Warning"/> is something that went wrong without stopping the scan - a page that
/// would not load, a storage read that failed, a write-back that could not complete. The
/// distinction matters to the window, which colours them differently, and to the console, which
/// sends warnings to stderr so a pipeline can separate them.
/// </remarks>
public interface IScanLog
{
    void Info(string message);

    void Warning(string message);
}
