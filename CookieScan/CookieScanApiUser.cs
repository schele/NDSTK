namespace NDSTK.CookieScan;

/// <summary>
/// Configuration for the cookie scanner's API user, bound from <c>NDSTK:CookieScanApiUser</c>.
/// </summary>
/// <remarks>
/// Opt-in by construction: with <see cref="Enabled"/> false or <see cref="ClientSecret"/> blank,
/// the seeder does nothing at all. This creates a credential with content access, so it must never
/// appear by default on an environment nobody asked for it on.
/// </remarks>
public sealed class CookieScanApiUserOptions
{
    public const string SectionName = "NDSTK:CookieScanApiUser";

    public bool Enabled { get; set; }

    public string ClientId { get; set; } = "cookie-scanner";

    /// <summary>Belongs in appsettings.Secrets.json, which is gitignored, or an environment variable.</summary>
    public string? ClientSecret { get; set; }

    public string Name { get; set; } = "Cookie scanner";

    public string Email { get; set; } = "cookie-scanner@ndstk.local";

    /// <summary>
    /// The user group aliases the API user joins. Content access is what the merge endpoint's
    /// authorisation requires; nothing here needs Settings or Users.
    /// </summary>
    public string[] UserGroupAliases { get; set; } = ["editor"];
}
