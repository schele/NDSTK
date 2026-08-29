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

    // The test that earns its keep: it fails when a file is renamed in one place and not the other -
    // a font, a component, the vendored Lit bundle - which is otherwise a blank page at runtime with
    // a 404 nobody sees.
    [Fact]
    public void Every_asset_the_index_references_resolves()
    {
        Assert.True(DashboardAssets.TryOpen("/index.html", out Stream content, out _));

        string html;

        using (var reader = new StreamReader(content))
        {
            html = reader.ReadToEnd();
        }

        MatchCollection references = Regex.Matches(html, @"(?:src|href)\s*=\s*""(?<path>[^""#:]+)""");

        Assert.NotEmpty(references);

        foreach (Match reference in references)
        {
            string path = reference.Groups["path"].Value;

            Assert.True(
                DashboardAssets.TryOpen(path.StartsWith('/') ? path : "/" + path, out Stream asset, out _),
                $"index.html references {path}, which is not embedded.");

            asset.Dispose();
        }
    }
}
