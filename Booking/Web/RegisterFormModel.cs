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

    // ---------------------------------------------------------------- the guardian

    [Required(ErrorMessage = "Ange ditt förnamn.")]
    [StringLength(100)]
    [Display(Name = "Ditt förnamn")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange ditt efternamn.")]
    [StringLength(100)]
    [Display(Name = "Ditt efternamn")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Shown on the class roster, so a coach can reach a parent.</summary>
    [Required(ErrorMessage = "Ange ditt telefonnummer.")]
    [StringLength(30)]
    [Display(Name = "Telefon")]
    public string Phone { get; set; } = string.Empty;

    // ------------------------------------------------------------------- the child

    [Required(ErrorMessage = "Ange barnets förnamn.")]
    [StringLength(100)]
    [Display(Name = "Barnets förnamn")]
    public string ChildFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets efternamn.")]
    [StringLength(100)]
    [Display(Name = "Barnets efternamn")]
    public string ChildLastName { get; set; } = string.Empty;

    /// <summary>
    /// Eight digits, ÅÅÅÅMMDD. The real date check lives in the controller, which is also where the
    /// "not in the future" rule is - a data annotation cannot express either.
    /// </summary>
    [Required(ErrorMessage = "Ange barnets födelsedatum.")]
    [Display(Name = "Barnets födelsedatum (ÅÅÅÅMMDD)")]
    public string ChildBirthDate { get; set; } = string.Empty;

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
