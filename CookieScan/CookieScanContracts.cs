namespace NDSTK.CookieScan;

/// <summary>One declaration the scanner proposes, as it arrives over the wire.</summary>
public sealed record CookieScanDeclaration(
    string Name,
    string Provider,
    string Category,
    string Purpose,
    string Duration,
    string StorageType);

/// <summary>
/// A merge request. <paramref name="DryRun"/> plans and reports without saving, which is what lets
/// an operator see exactly what would change before allowing it.
/// </summary>
public sealed record CookieScanMergeRequest(
    IReadOnlyList<CookieScanDeclaration> Declarations,
    bool DryRun = false);

public sealed record CookieScanMergeResponse(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    Guid PolicyPageKey,
    bool Saved);
