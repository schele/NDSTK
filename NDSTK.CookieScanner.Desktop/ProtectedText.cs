using System.Security.Cryptography;
using System.Text;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// One string in and out of a DPAPI blob, in the shape the settings file stores it.
/// </summary>
/// <remarks>
/// Its own file rather than a private corner of <see cref="DashboardSettings"/>: the settings record
/// is about what the window remembers, and a test that wants to know whether a value is really
/// ciphertext should be able to ask this directly.
/// <para>
/// <see cref="DataProtectionScope.CurrentUser"/>, so the key belongs to the Windows account that ran
/// the window. That is what the whole scheme is worth: the file is unreadable to another user on the
/// machine, to another machine, and to anyone the file is copied to. It is NOT worth anything against
/// code running as this user - DPAPI hands that code the same plaintext it hands this class, by
/// design. It is at-rest protection for a settings file, not a vault.
/// </para>
/// <para>
/// The stored form carries a prefix rather than being bare base64, so a reader - a person, a later
/// build, or <see cref="DashboardSettings.Load(string)"/> itself - can tell a blob from a value the
/// pre-profiles build wrote in plain text. Without it, a legacy client id that happened to be valid
/// base64 would be handed to DPAPI and come back as a decrypt failure rather than as the plain
/// string it is.
/// </para>
/// </remarks>
public static class ProtectedText
{
    /// <summary>What every protected value in the settings file begins with.</summary>
    public const string Prefix = "dpapi:";

    /// <summary>
    /// The application-specific entropy mixed into every blob.
    /// </summary>
    /// <remarks>
    /// Not a secret, and it does not need to be: it ships in the exe, and anyone who can read it can
    /// already run code as the user whose key would open the blob. It is a namespace - a blob
    /// produced by some other program under the same Windows account will not open with it, so a
    /// value pasted from another application's config cannot be fed in by mistake and quietly
    /// decrypt into a field this window would then use as a password.
    /// <para>
    /// It is versioned in its own text because changing it invalidates every blob already on disk.
    /// If it ever has to change, bump the suffix and expect one round of "could not be read"
    /// warnings rather than trying to keep the two compatible.
    /// </para>
    /// </remarks>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NDSTK.CookieScanner/settings/v1");

    /// <summary>The prefixed base64 blob for a value, or an empty string for an empty value.</summary>
    /// <remarks>
    /// Blank in, blank out. Encrypting an empty string would put a forty-byte blob in the file for
    /// every field the operator left alone, which makes the file unreadable for nothing and hides
    /// which fields are actually set.
    /// </remarks>
    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        byte[] blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Reads one stored value back, answering false for anything this user cannot open.
    /// </summary>
    /// <remarks>
    /// False rather than a throw, because every caller's answer to a blob that will not open is the
    /// same: treat the field as empty and say so once. The three ways it happens - a file copied from
    /// another Windows account or another machine, a blob edited by hand, and a value with no prefix
    /// at all - are deliberately not distinguished: they all mean the same thing to the operator, who
    /// has to type the value again either way.
    /// <para>
    /// An empty stored value is true with an empty result, not a failure: it is a field that was
    /// never set, which is not something to warn about.
    /// </para>
    /// </remarks>
    public static bool TryUnprotect(string? stored, out string value)
    {
        value = "";

        if (string.IsNullOrEmpty(stored))
        {
            return true;
        }

        if (stored.StartsWith(Prefix, StringComparison.Ordinal) is false)
        {
            return false;
        }

        try
        {
            byte[] blob = Convert.FromBase64String(stored[Prefix.Length..]);

            value = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));

            return true;
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            // FormatException is base64 that is not base64; CryptographicException is everything
            // DPAPI itself refuses - the wrong user, the wrong machine, the wrong entropy, a
            // tampered blob. Narrow on purpose: anything else here is a bug in this class rather
            // than a file that cannot be read, and Load's own catch is the backstop for that.
            return false;
        }
    }
}
