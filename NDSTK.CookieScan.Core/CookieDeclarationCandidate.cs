namespace NDSTK.CookieScan.Core;

/// <summary>How much a candidate needs a human to look at it.</summary>
public enum CandidateFlag
{
    /// <summary>Categorised with evidence. Safe to add.</summary>
    None,

    /// <summary>Set in a pass that had not granted its category. Reported first; fails the run.</summary>
    Violation,

    /// <summary>Only ever seen with everything granted, and unrecognised. Category is a fallback.</summary>
    NeedsReview,
}

/// <summary>
/// A declaration the scan proposes for the policy page, in the exact shape a
/// <c>cookieDefinition</c> block needs.
/// </summary>
/// <remarks>
/// <paramref name="Category"/> and <paramref name="StorageType"/> hold the package's wire values
/// verbatim - lowercase category names, mixed-case storage names - because the merge endpoint
/// validates against those and writes them straight into the block.
/// </remarks>
public sealed record CookieDeclarationCandidate(
    string Name,
    string Provider,
    string Category,
    string Purpose,
    string Duration,
    string StorageType,
    CandidateFlag Flag,
    ConsentPass FirstSeenPass,
    string FirstSeenUrl);
