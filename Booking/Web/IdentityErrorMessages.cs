using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace NDSTK.Booking.Web;

/// <summary>
/// Turns ASP.NET Identity's error codes into Swedish, member-facing text.
/// </summary>
/// <remarks>
/// Umbraco localises some identity errors through <c>UmbracoErrorDescriberBase</c> and
/// <c>ILocalizedTextService</c>, but not the password ones - those fall through to Identity's own
/// English resources, which is why a member saw "The password must be at least 10 characters long".
///
/// Mapped here rather than by registering a custom <see cref="IdentityErrorDescriber"/> because
/// registration is the only place an Identity error reaches a member: the login controller writes
/// its own Swedish messages, and everything else is either a redirect or a log line. Doing it here
/// keeps the member-facing copy in one file, next to the form it belongs to.
///
/// The rule text is built from the live password configuration, so raising
/// <c>Umbraco:CMS:Security:MemberPassword:RequiredLength</c> changes the message with it rather than
/// leaving a stale number behind.
/// </remarks>
public sealed class IdentityErrorMessages(IOptionsMonitor<MemberPasswordConfigurationSettings> passwordOptions)
{
    /// <summary>
    /// IMemberPasswordConfiguration is not in the container; the bound settings object is. Read per
    /// call rather than cached so a configuration reload takes effect without a restart.
    /// </summary>
    private MemberPasswordConfigurationSettings Rules => passwordOptions.CurrentValue;

    /// <summary>
    /// Swedish text for one Identity error. Falls back to a generic sentence rather than leaking the
    /// English original, so a code that is not mapped yet still reads as the club's own site.
    /// </summary>
    public string Describe(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" =>
            $"Lösenordet måste vara minst {Rules.RequiredLength} tecken långt.",
        "PasswordRequiresDigit" => "Lösenordet måste innehålla minst en siffra.",
        "PasswordRequiresLower" => "Lösenordet måste innehålla minst en liten bokstav.",
        "PasswordRequiresUpper" => "Lösenordet måste innehålla minst en stor bokstav.",
        "PasswordRequiresNonAlphanumeric" =>
            "Lösenordet måste innehålla minst ett specialtecken, till exempel ! eller ?.",
        "PasswordRequiresUniqueChars" => "Lösenordet måste innehålla fler olika tecken.",
        "PasswordMismatch" => "Lösenorden matchar inte.",

        "InvalidEmail" => "E-postadressen ser inte riktig ut.",
        "InvalidUserName" => "E-postadressen innehåller tecken som inte kan användas.",

        // Mapped for completeness, but the registration flow never shows these: a duplicate address
        // deliberately gets the same response as success, so the form cannot be used to discover who
        // is a member of the club.
        "DuplicateUserName" or "DuplicateEmail" =>
            "Det gick inte att skapa kontot med den adressen.",

        _ => "Något gick fel. Försök igen om en liten stund.",
    };

    /// <summary>
    /// A single sentence describing the password rules, for the form to show up front. Telling a
    /// member the requirements before they choose beats rejecting them afterwards.
    /// </summary>
    public string PasswordRules()
    {
        List<string> parts = [$"minst {Rules.RequiredLength} tecken"];

        if (Rules.RequireDigit)
        {
            parts.Add("en siffra");
        }

        if (Rules.RequireLowercase)
        {
            parts.Add("en liten bokstav");
        }

        if (Rules.RequireUppercase)
        {
            parts.Add("en stor bokstav");
        }

        if (Rules.RequireNonLetterOrDigit)
        {
            parts.Add("ett specialtecken");
        }

        return parts.Count == 1
            ? $"Lösenordet måste vara {parts[0]}."
            : $"Lösenordet måste innehålla {string.Join(", ", parts[..^1])} och {parts[^1]}.";
    }
}
