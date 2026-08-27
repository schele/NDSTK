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
            // Nothing is read on a failed navigation, deliberately. The page is still sitting on
            // the PREVIOUS url, so a storage read here is same-origin, does not throw, and returns
            // the previous page's keys labelled as this one's. A cookie would be misattributed the
            // same way - the caller records the url an entry was first seen at, and that would name
            // a page which never loaded. Anything genuinely set by the previous page's late
            // resources is still in the context and gets picked up by the next successful visit.
            Console.Error.WriteLine(
                $"  skipped {url} - it did not load ({error.Message.Split('\n')[0]})");

            return new PageObservation([], new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase));
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
        catch (PlaywrightException error)
        {
            // Storage access legitimately throws on an opaque origin and on an error page, so this
            // is not fatal. It is still logged: the same exception type covers "execution context
            // was destroyed" and a closed target, and an unlogged catch here would make a real
            // capture failure look identical to a page that simply stores nothing - which is
            // under-reporting with no trace, the one outcome this tool must never produce.
            Console.Error.WriteLine(
                $"  could not read {store} on {page.Url} ({error.Message.Split('\n')[0]})");

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
