namespace NDSTK.CookieScan.Core;

/// <summary>
/// The consent state a scan pass ran under. Declared in the order the passes run, because the
/// earliest pass an entry appeared in is what decides its category.
/// </summary>
public enum ConsentPass
{
    Undecided = 0,
    RejectAll = 1,
    Preferences = 2,
    Statistics = 3,
    Marketing = 4,
    AcceptAll = 5,

    /// <summary>
    /// The signed-in dimension. Deliberately outside the comparable sequence: it visits a
    /// different URL set, so its findings cannot be compared by pass order against the six.
    /// </summary>
    MemberArea = 6,
}

/// <summary>
/// What each pass granted, and what an entry first appearing in it therefore implies.
/// </summary>
public static class ConsentPasses
{
    /// <summary>The six passes that share one URL list and are therefore comparable by order.</summary>
    public static readonly IReadOnlyList<ConsentPass> Comparable =
    [
        ConsentPass.Undecided,
        ConsentPass.RejectAll,
        ConsentPass.Preferences,
        ConsentPass.Statistics,
        ConsentPass.Marketing,
        ConsentPass.AcceptAll,
    ];

    /// <summary>
    /// The categories granted during a pass. The violation rule compares a cookie's catalogued
    /// category against this set: a statistics cookie appearing while only preferences was granted
    /// is a violation just as plainly as one appearing after a flat refusal.
    /// </summary>
    public static IReadOnlySet<string> Granted(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => Set(),
        ConsentPass.RejectAll => Set(),
        ConsentPass.Preferences => Set("preferences"),
        ConsentPass.Statistics => Set("statistics"),
        ConsentPass.Marketing => Set("marketing"),
        ConsentPass.AcceptAll => Set("preferences", "statistics", "marketing"),
        ConsentPass.MemberArea => Set("preferences", "statistics", "marketing"),
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };

    /// <summary>
    /// The category implied by an entry first appearing in this pass, or <c>null</c> when the pass
    /// implies nothing. Only <see cref="ConsentPass.AcceptAll"/> returns null: it grants
    /// everything, so an entry first seen there could belong to any of the three.
    /// </summary>
    public static string? ImpliedCategory(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => "necessary",
        ConsentPass.RejectAll => "necessary",
        ConsentPass.Preferences => "preferences",
        ConsentPass.Statistics => "statistics",
        ConsentPass.Marketing => "marketing",
        ConsentPass.AcceptAll => null,

        // A cookie that only exists once you are signed in is a session cookie by construction.
        ConsentPass.MemberArea => "necessary",
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };

    private static IReadOnlySet<string> Set(params string[] categories)
        => new HashSet<string>(categories, StringComparer.Ordinal);
}
