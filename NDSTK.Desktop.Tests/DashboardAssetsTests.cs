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
}
