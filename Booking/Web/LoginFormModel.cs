using System.ComponentModel.DataAnnotations;

namespace NDSTK.Booking.Web;

/// <summary>The login form. Email doubles as the username, so there is only one identity field.</summary>
public sealed class LoginFormModel
{
    [Required(ErrorMessage = "Ange din e-postadress.")]
    [EmailAddress(ErrorMessage = "E-postadressen ser inte riktig ut.")]
    [Display(Name = "E-postadress")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange ditt lösenord.")]
    [DataType(DataType.Password)]
    [Display(Name = "Lösenord")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Håll mig inloggad")]
    public bool RememberMe { get; set; }
}
