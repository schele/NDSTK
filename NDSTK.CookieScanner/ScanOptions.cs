using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>The parsed command line.</summary>
/// <remarks>
/// <paramref name="ClientSecret"/> comes from the environment, never from a flag: a secret passed
/// as an argument ends up in shell history and in any process listing.
/// </remarks>
public sealed record ScanOptions(
    Uri Url,
    Uri Target,
    int MaxPages,
    Locale Locale,
    string? MemberEmail,
    string? MemberPassword,
    string? ClientId,
    string? ClientSecret,
    bool DryRun,
    string ReportDir,
    bool Headed)
{
    public const string SecretVariable = "NDSTK_COOKIESCAN_CLIENT_SECRET";

    /// <summary>
    /// Whether the endpoint can be called at all. Report-only is the safe default: a missing
    /// credential is not an error, it just means the scan cannot compare itself against the page.
    /// </summary>
    public bool CanReachApi
        => string.IsNullOrWhiteSpace(ClientId) is false
            && string.IsNullOrWhiteSpace(ClientSecret) is false;

    public bool MemberScanEnabled
        => string.IsNullOrWhiteSpace(MemberEmail) is false
            && string.IsNullOrWhiteSpace(MemberPassword) is false;

    public static ScanOptions Parse(string[] args)
    {
        Dictionary<string, string?> flags = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal) is false)
            {
                continue;
            }

            string key = args[index][2..];
            bool hasValue = index + 1 < args.Length
                && args[index + 1].StartsWith("--", StringComparison.Ordinal) is false;

            flags[key] = hasValue ? args[++index] : null;
        }

        if (flags.TryGetValue("url", out string? url) is false || string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException(
                "--url is required. Example: ndstk-cookiescan --url https://ndstk.se");
        }

        Uri root = Absolute(url, "url");

        return new ScanOptions(
            Url: root,
            Target: flags.TryGetValue("target", out string? target) && string.IsNullOrWhiteSpace(target) is false
                ? Absolute(target, "target")
                : root,
            MaxPages: flags.TryGetValue("max-pages", out string? maxPages)
                && int.TryParse(maxPages, out int parsed) && parsed > 0
                ? parsed
                : 25,
            Locale: flags.TryGetValue("locale", out string? locale)
                && string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
                ? Locale.En
                : Locale.Sv,
            MemberEmail: Value(flags, "member-email"),
            MemberPassword: Value(flags, "member-password"),
            ClientId: Value(flags, "client-id"),
            ClientSecret: Environment.GetEnvironmentVariable(SecretVariable),
            DryRun: flags.ContainsKey("dry-run"),
            ReportDir: Value(flags, "report-dir") ?? Directory.GetCurrentDirectory(),
            Headed: flags.ContainsKey("headed"));

        static string? Value(Dictionary<string, string?> flags, string key)
            => flags.TryGetValue(key, out string? value) && string.IsNullOrWhiteSpace(value) is false
                ? value
                : null;

        // UriFormatException derives from FormatException, not ArgumentException, so an
        // unvalidated constructor call here escapes Program's single catch and greets a mistyped
        // URL with a stack trace. A URL pasted without its scheme is the likeliest operator
        // error there is, so it gets a message that names that cause.
        static Uri Absolute(string value, string flag)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
            {
                return parsed;
            }

            throw new ArgumentException(
                $"--{flag} is not an absolute URL: '{value}'. It needs a scheme, for example "
                + $"--{flag} https://ndstk.se");
        }
    }
}
