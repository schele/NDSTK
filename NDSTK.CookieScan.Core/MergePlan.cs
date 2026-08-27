namespace NDSTK.CookieScan.Core;

/// <summary>
/// What a merge would do, worked out before anything is written.
/// </summary>
/// <remarks>
/// Deliberately a plan rather than an action: the tool prints it, the endpoint validates it, and
/// both work off exactly the same computation. Nothing here deletes or updates - every list other
/// than <paramref name="ToAdd"/> exists to be reported to a human.
/// </remarks>
public sealed record MergePlan(
    IReadOnlyList<CookieDeclarationCandidate> ToAdd,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    IReadOnlyList<string> ExpectedButNotObserved)
{
    /// <summary>
    /// True when the plan proposes more blocks than one call may add. The endpoint turns this into
    /// a 400 and writes nothing: past this many, something is wrong with the scan or the
    /// catalogue, and half-applying it would be worse than refusing.
    /// </summary>
    public bool ExceedsCap => ToAdd.Count > MergePlanner.MaxBlocksPerCall;

    /// <summary>True when there is anything to write at all.</summary>
    public bool HasWork => ToAdd.Count > 0;
}
