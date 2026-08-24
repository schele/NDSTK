using System.ComponentModel.DataAnnotations;

namespace NDSTK.Booking.Web;

/// <summary>
/// The registration form. Validation messages are Swedish because the member reads them.
/// </summary>
public sealed class RegisterFormModel
{
    [Required(ErrorMessage = "Ange din e-postadress.")]
    [EmailAddress(ErrorMessage = "E-postadressen ser inte riktig ut.")]
    [Display(Name = "E-postadress")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Välj ett lösenord.")]
    [DataType(DataType.Password)]
    [Display(Name = "Lösenord")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Upprepa lösenordet.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Lösenorden matchar inte.")]
    [Display(Name = "Upprepa lösenord")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    /// Honeypot. Hidden from people with CSS, so a human leaves it empty and a naive bot that fills
    /// every input does not. Named to look worth filling in.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// When the form was rendered, as Unix seconds, round-tripped through a hidden field. A
    /// submission that arrives implausibly fast was not typed by a person. Unsigned on purpose:
    /// forging it only re-enables a form the attacker could have submitted anyway, so a signature
    /// would add machinery without adding protection. The real defences are the rate limiter and
    /// the password policy.
    /// </summary>
    public long RenderedAt { get; set; }
}
