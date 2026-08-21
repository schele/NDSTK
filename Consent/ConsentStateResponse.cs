namespace NDSTK.Consent;

/// <summary>
/// Canonical consent state after a decision. The banner uses this to unblock scripts without a reload,
/// so it must reflect what the server actually stored, not what the client asked for.
/// </summary>
public sealed record ConsentStateResponse(
    int Version,
    string[] Categories,
    string ConsentId,
    string DecidedAt);
