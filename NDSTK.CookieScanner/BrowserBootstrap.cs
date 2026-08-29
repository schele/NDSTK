namespace NDSTK.CookieScanner;

/// <summary>
/// Makes sure a Chromium build exists before the scan starts, fetching one if it does not.
/// </summary>
/// <remarks>
/// Chromium is not inside the exe - it lives in <c>%LOCALAPPDATA%\ms-playwright</c> and is roughly
/// 150MB. Doing this here rather than telling the user to run a separate install command is the
/// difference between a copy-anywhere exe and one with a setup ritual, and the message below is
/// why a first run appears to hang for a minute.
/// </remarks>
public static class BrowserBootstrap
{
    public static void EnsureChromium(IScanLog log)
    {
        // Playwright's own installer is idempotent and cheap when the browser is already present,
        // so there is no need to probe for it first - and no need to guess at the cache path,
        // which differs per platform and per Playwright version.
        log.Info("Checking for a Chromium build...");

        int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not install Chromium (Playwright exited {exitCode}). The first run on a "
                + "new machine downloads roughly 150MB, so this needs internet access. Once it has "
                + "succeeded, later runs reuse the copy in %LOCALAPPDATA%\\ms-playwright.");
        }
    }
}
