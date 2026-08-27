namespace NDSTK.CookieScan.Core;

/// <summary>Where a scanned entry was stored in the browser.</summary>
public enum StorageKind
{
    Cookie,
    LocalStorage,
    SessionStorage,
}

/// <summary>
/// Wire names for <see cref="StorageKind"/>, matching the CookieBanner package's "Storage type"
/// dropdown exactly.
/// </summary>
/// <remarks>
/// The dropdown offers <c>Cookie</c>, <c>localStorage</c>, <c>sessionStorage</c> and <c>Pixel</c> -
/// mixed case, and not derivable from the enum member names. Kept as an explicit map so renaming a
/// member here cannot silently write a value the dropdown will not accept. The scanner never emits
/// <c>Pixel</c>; see the spec's non-goals.
/// </remarks>
public static class StorageKinds
{
    public static string ToWireName(StorageKind kind) => kind switch
    {
        StorageKind.Cookie => "Cookie",
        StorageKind.LocalStorage => "localStorage",
        StorageKind.SessionStorage => "sessionStorage",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
