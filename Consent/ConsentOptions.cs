namespace NDSTK.Consent;

/// <summary>
/// Bound from the <c>Ndstk:Consent</c> configuration section.
/// </summary>
public sealed class ConsentOptions
{
    public const string SectionName = "Ndstk:Consent";

    /// <summary>
    /// Version of the cookie text. Bumping this re-prompts every visitor, so it is configuration
    /// rather than a constant: changing the policy wording is a deploy-time decision, not a code change.
    /// </summary>
    public int PolicyVersion { get; set; } = 1;

    public string CookieName { get; set; } = "ndstk-consent";

    public int CookieLifetimeDays { get; set; } = 365;

    /// <summary>
    /// Google measurement id. When null — the current state of this site — no Consent Mode snippet is
    /// emitted at all, rather than shipping dead script to every page.
    /// </summary>
    public string? GoogleMeasurementId { get; set; }
}
