using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>The options that shaped a scan, as much of them as a comparison needs.</summary>
/// <remarks>
/// Recorded because two scans run with different options diff as though the site had changed: a
/// member scan finds the member cookie and a public scan does not, which is a property of the run
/// rather than of the site. Nullable, because a history file written before this existed must load
/// and say "not recorded" instead of claiming a default nobody chose.
/// <para>
/// Deliberately not the whole <see cref="ScanOptions"/>: that record carries the client secret and
/// the member password, and neither may ever reach a file.
/// </para>
/// </remarks>
public sealed record ScanOptionsSummary(int MaxPages, Locale Locale, bool MemberScanEnabled, bool DryRun);

/// <summary>
/// Everything one scan produced. Serialized as-is to both the report file and the history
/// folder, so it must stay round-trippable: no computed collections, no types System.Text.Json
/// cannot rebuild.
/// </summary>
public sealed record ScanResult(
    IReadOnlyList<CookieDeclarationCandidate> Candidates,
    IReadOnlyList<CookieDeclarationCandidate> Violations,
    IReadOnlyList<string> ExpectedButNotObserved,
    IReadOnlyDictionary<ConsentPass, IReadOnlyList<string>> HostsByPass,
    MergeOutcome? Outcome,
    bool CanReachApi,
    bool DryRun,
    DateTimeOffset CompletedAt,
    string Site,
    ScanOptionsSummary? Options = null)
{
    /// <summary>
    /// The process exit code. Findings outrank plumbing, and configuration is never an error on
    /// its own.
    /// </summary>
    /// <remarks>
    /// A missing credential returns 0 because report-only is a supported mode. A write-back that
    /// was configured, attempted and failed returns 2, because a CI job gating on this would
    /// otherwise stay green while the policy page silently stopped being updated. An empty scan
    /// never posts at all, so a null outcome there means "nothing to send" rather than "failed".
    /// </remarks>
    [JsonIgnore]
    public int ExitCode =>
        Violations.Count > 0 ? 1
        : Outcome is null && CanReachApi && Candidates.Count > 0 ? 2
        : 0;

    /// <summary>Candidates the scan could not attribute to a single category.</summary>
    /// <remarks>
    /// Derived rather than stored, so the serialized form has one source of truth for a
    /// candidate's flag.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<CookieDeclarationCandidate> NeedsReview =>
        [.. Candidates.Where(candidate => candidate.Flag == CandidateFlag.NeedsReview)];
}
