using Microsoft.Playwright;

namespace NDSTK.CookieScanner;

/// <summary>
/// Bounded breadth-first discovery of the site's own HTML pages.
/// </summary>
/// <remarks>
/// The list this produces is replayed identically by every consent pass. That is a correctness
/// requirement rather than an optimisation: if each pass discovered its own URLs, an entry
/// appearing "first in pass 4" might only mean pass 4 was the first to visit the page that sets
/// it, and every category inference downstream would be wrong.
/// </remarks>
public sealed class SiteCrawler(IPage page, ScanOptions options)
{
    public async Task<IReadOnlyList<Uri>> DiscoverAsync(Uri from)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<Uri> ordered = [];
        Queue<Uri> queue = new();

        queue.Enqueue(from);
        seen.Add(Normalise(from));

        while (queue.Count > 0 && ordered.Count < options.MaxPages)
        {
            Uri current = queue.Dequeue();

            IResponse? response;

            try
            {
                response = await page.GotoAsync(
                    current.ToString(),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20_000 });
            }
            catch (PlaywrightException error)
            {
                // A page that will not load is worth a line, not an abort: one broken link must
                // not cost the whole scan.
                Console.Error.WriteLine($"  skipped {current} ({error.Message.Split('\n')[0]})");
                continue;
            }

            // Only HTML sets cookies through markup and script. A PDF or an image would just
            // burn a slot from the page cap.
            string contentType = response?.Headers.GetValueOrDefault("content-type") ?? string.Empty;

            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            ordered.Add(current);

            string[] hrefs = await page.EvalOnSelectorAllAsync<string[]>(
                "a[href]", "elements => elements.map(element => element.href)");

            foreach (string href in hrefs)
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out Uri? link) is false
                    || Exclusions.IsExcluded(link, options.Url))
                {
                    continue;
                }

                if (seen.Add(Normalise(link)))
                {
                    queue.Enqueue(link);
                }
            }
        }

        return ordered;
    }

    // Fragments address a position on a page already queued, so keeping them would spend the page
    // cap revisiting the same document.
    private static string Normalise(Uri url)
        => new UriBuilder(url) { Fragment = string.Empty }.Uri.ToString().TrimEnd('/');

    /// <summary>What the crawl refuses to follow, and why.</summary>
    public static class Exclusions
    {
        // Following one of these mid-crawl would end the member session and quietly make every
        // later page in that pass anonymous - a whole pass of wrong results, with no error.
        private static readonly string[] SignOutSegments = ["logout", "logga-ut", "signout", "sign-out"];

        public static bool IsExcluded(Uri candidate, Uri root)
        {
            if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            {
                return true;
            }

            if (candidate.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase) is false)
            {
                return true;
            }

            // Backoffice cookies do not belong in a visitor-facing policy.
            if (candidate.AbsolutePath.StartsWith("/umbraco", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SignOutSegments.Any(segment =>
                candidate.AbsolutePath.Contains(segment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
