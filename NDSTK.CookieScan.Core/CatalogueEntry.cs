using System.Text.Json.Serialization;

namespace NDSTK.CookieScan.Core;

/// <summary>
/// One row of the cookie catalogue: what a recognised name is, who sets it, and what to write
/// about it on the policy page.
/// </summary>
/// <remarks>
/// <paramref name="DurationDays"/> is machine-readable rather than pre-written text so that
/// <c>DurationFormatter</c> can render it in the requested locale - the spec's original
/// "24 månader" string could not honour an English run. <c>0</c> means a session cookie;
/// <c>null</c> means no documented lifetime, so use what the browser reported.
/// </remarks>
public sealed record CatalogueEntry(
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("provider")] LocalisedText Provider,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("purpose")] LocalisedText Purpose,
    [property: JsonPropertyName("durationDays")] int? DurationDays = null,
    [property: JsonPropertyName("tracker")] bool Tracker = false,
    [property: JsonPropertyName("expected")] bool Expected = false);
