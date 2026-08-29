using System.Reflection;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The dashboard's files, which live inside the exe.
/// </summary>
/// <remarks>
/// Embedded rather than copied beside the exe: the published build is a single file, and a folder of
/// loose assets beside it would have to survive an extraction directory Microsoft documents as not
/// recommended. Nothing here touches the disk.
/// </remarks>
public static class DashboardAssets
{
    private const string Root = "wwwroot";

    private static readonly Assembly Assembly = typeof(DashboardAssets).Assembly;

    /// <summary>Opens the asset a request path names, or reports that there is none.</summary>
    public static bool TryOpen(string path, out Stream content, out string contentType)
    {
        content = Stream.Null;
        contentType = "application/octet-stream";

        string relative = path.TrimStart('/');

        if (relative.Length == 0)
        {
            relative = "index.html";
        }

        // A resource name is not a file path, so ".." cannot escape a directory here - but it can
        // still name a resource outside wwwroot, which is the same mistake with a different shape.
        if (relative.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        Stream? stream = Assembly.GetManifestResourceStream($"{Root}/{relative}");

        if (stream is null)
        {
            return false;
        }

        content = stream;
        contentType = ContentType(relative);

        return true;
    }

    private static string ContentType(string relative) => Path.GetExtension(relative).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
