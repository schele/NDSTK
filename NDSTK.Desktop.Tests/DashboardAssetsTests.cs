using System.Text.RegularExpressions;

using NDSTK.CookieScanner.Desktop;

namespace NDSTK.Desktop.Tests;

public class DashboardAssetsTests
{
    [Fact]
    public void The_index_page_resolves()
    {
        Assert.True(DashboardAssets.TryOpen("/index.html", out Stream content, out string contentType));

        using (content)
        {
            Assert.Equal("text/html; charset=utf-8", contentType);
            Assert.NotEqual(0, content.Length);
        }
    }

    // The root path is what the window navigates to if a URL ever loses its filename; serving the
    // index there costs one line and turns a blank window into a working one.
    [Fact]
    public void The_root_path_resolves_to_the_index()
    {
        Assert.True(DashboardAssets.TryOpen("/", out Stream content, out _));

        content.Dispose();
    }

    [Fact]
    public void An_unknown_path_does_not_resolve()
    {
        Assert.False(DashboardAssets.TryOpen("/nope.js", out _, out _));
    }

    // A path that climbs out of wwwroot must not reach another embedded resource.
    [Fact]
    public void A_traversing_path_does_not_resolve()
    {
        Assert.False(DashboardAssets.TryOpen("/../NDSTK.CookieScanner.Desktop.dll", out _, out _));
    }

    // Uri.AbsolutePath hands back percent-encoded octets undecoded, so the resolver decodes before
    // it looks anything up - otherwise every asset with a space or an accent in its name 404s.
    [Fact]
    public void A_percent_encoded_path_resolves()
    {
        Assert.True(DashboardAssets.TryOpen("/index%2Ehtml", out Stream content, out _));

        content.Dispose();
    }

    // Decoding happens before the traversal guard, not after: %2E%2E contains no ".." until it is
    // decoded, so a guard that ran first would wave this through.
    [Fact]
    public void An_encoded_traversing_path_does_not_resolve()
    {
        Assert.False(DashboardAssets.TryOpen("/%2E%2E/NDSTK.CookieScanner.Desktop.dll", out _, out _));
    }

    // The test that earns its keep: it fails when a file is renamed in one place and not the other -
    // a font, a component, the vendored Lit bundle - which is otherwise a blank page at runtime with a
    // 404 nobody sees. It crawls rather than reading index.html alone, because index.html names only
    // the stylesheet and the module: the font is reached through a @font-face inside the stylesheet
    // and the components through ES imports, so a single-file check would see almost nothing.
    [Fact]
    public void Every_asset_reachable_from_the_index_resolves()
    {
        var queue = new Queue<string>(["/index.html"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var crawled = new List<string>();

        while (queue.Count > 0)
        {
            string path = queue.Dequeue();

            if (seen.Add(path) is false)
            {
                continue;
            }

            Assert.True(
                DashboardAssets.TryOpen(path, out Stream content, out _),
                $"{path} is referenced but not embedded.");

            string text;

            using (var reader = new StreamReader(content))
            {
                text = reader.ReadToEnd();
            }

            crawled.Add(path);

            foreach (string reference in References(path, text))
            {
                queue.Enqueue(reference);
            }
        }

        // A crawl that finds nothing passes vacuously, which is how this test could rot into silence.
        Assert.True(crawled.Count >= 3, $"crawled only: {string.Join(", ", crawled)}");
    }

    // Only rooted references are followed: "#scan", "https://..." and the like are not assets.
    private static IEnumerable<string> References(string path, string text)
    {
        string? pattern = Path.GetExtension(path) switch
        {
            ".html" => @"(?:src|href)\s*=\s*""(?<path>[^""]+)""",
            ".css" => @"url\(\s*['""]?(?<path>[^'"")]+)['""]?\s*\)",
            ".js" => @"from\s+['""](?<path>[^'""]+)['""]",
            _ => null,
        };

        if (pattern is null)
        {
            return [];
        }

        return Regex.Matches(text, pattern)
            .Select(match => match.Groups["path"].Value)
            .Where(reference => reference.StartsWith('/'));
    }
}
