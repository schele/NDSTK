namespace NDSTK.Consent;

/// <summary>
/// The four consent categories. <see cref="Necessary"/> is never declinable and is implied rather
/// than stored, so it must not appear in the cookie's category list.
/// </summary>
public enum ConsentCategory
{
    Necessary,
    Preferences,
    Statistics,
    Marketing,
}
