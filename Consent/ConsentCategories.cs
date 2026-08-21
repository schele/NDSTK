namespace NDSTK.Consent;

/// <summary>
/// Wire names for <see cref="ConsentCategory"/>. Kept as an explicit map rather than
/// <c>Enum.ToString</c> so that renaming a member cannot silently invalidate every cookie already
/// in the wild.
/// </summary>
public static class ConsentCategories
{
    /// <summary>The categories a visitor can actually choose, in banner display order.</summary>
    public static readonly IReadOnlyList<ConsentCategory> Consentable =
    [
        ConsentCategory.Preferences,
        ConsentCategory.Statistics,
        ConsentCategory.Marketing,
    ];

    /// <summary>All categories in policy-page display order, necessary first.</summary>
    public static readonly IReadOnlyList<ConsentCategory> All =
    [
        ConsentCategory.Necessary,
        ConsentCategory.Preferences,
        ConsentCategory.Statistics,
        ConsentCategory.Marketing,
    ];

    public static string ToWireName(ConsentCategory category) => category switch
    {
        ConsentCategory.Necessary => "necessary",
        ConsentCategory.Preferences => "preferences",
        ConsentCategory.Statistics => "statistics",
        ConsentCategory.Marketing => "marketing",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static bool TryParse(string? wireName, out ConsentCategory category)
    {
        switch (wireName)
        {
            case "necessary": category = ConsentCategory.Necessary; return true;
            case "preferences": category = ConsentCategory.Preferences; return true;
            case "statistics": category = ConsentCategory.Statistics; return true;
            case "marketing": category = ConsentCategory.Marketing; return true;
            default: category = default; return false;
        }
    }
}
