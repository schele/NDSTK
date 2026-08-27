using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>One thing found in the browser, before a pass is attributed to it.</summary>
public sealed record CapturedEntry(string Name, StorageKind Storage, DateTimeOffset? Expires);

/// <summary>What one page visit produced.</summary>
public sealed record PageObservation(IReadOnlyList<CapturedEntry> Entries, IReadOnlySet<string> Hosts);

/// <summary>
/// Visits one page and reads back everything the browser now holds.
/// </summary>
public static class PageCapture
{
    public static async Task<PageObservation> VisitAsync(IPage page, Uri url, ISet<string> hosts)
    {
        try
        {
            // NetworkIdle rather than DOMContentLoaded: a third-party tag that sets a cookie
            // usually loads after the document is parsed, and stopping earlier would miss exactly
            // the cookies this tool exists to find.
            await page.GotoAsync(
                url.ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 });
        }
        catch (PlaywrightException error)
        {
            Console.Error.WriteLine($"  {url} did not settle ({error.Message.Split('\n')[0]})");
        }

        List<CapturedEntry> entries = [];

        // Read from the context, not the page: a cookie set for the whole site by an earlier page
        // in this pass belongs to this pass, and reading per-page would keep re-finding it.
        foreach (BrowserContextCookiesResult cookie in await page.Context.CookiesAsync())
        {
            entries.Add(new CapturedEntry(
                cookie.Name,
                StorageKind.Cookie,
                // Playwright reports -1 for a session cookie.
                cookie.Expires < 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires)));
        }

        entries.AddRange(await KeysAsync(page, "localStorage", StorageKind.LocalStorage));
        entries.AddRange(await KeysAsync(page, "sessionStorage", StorageKind.SessionStorage));

        return new PageObservation(entries, new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<CapturedEntry>> KeysAsync(
        IPage page, string store, StorageKind kind)
    {
        try
        {
            string[] keys = await page.EvaluateAsync<string[]>($"() => Object.keys({store})");

            // Neither store has an expiry; DurationFormatter decides the wording from the kind.
            return keys.Select(key => new CapturedEntry(key, kind, null)).ToArray();
        }
        catch (PlaywrightException)
        {
            // Storage access throws on a page served from an opaque origin, and on an error page.
            // Nothing to report - it is an absence of data, not a fault.
            return [];
        }
    }

    /// <summary>
    /// Records the host of every request the page makes, for the report's third-party section.
    /// Attach once per context, before any navigation.
    /// </summary>
    public static void RecordHosts(IPage page, ISet<string> hosts, Uri root)
    {
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri)
                && uri.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase) is false)
            {
                hosts.Add(uri.Host);
            }
        };
    }
}
