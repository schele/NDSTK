using System.ComponentModel.DataAnnotations;

namespace NDSTK.Booking.Web;

/// <summary>Adding or editing one child. The key is empty when adding.</summary>
public sealed class ParticipantFormModel
{
    public Guid Key { get; set; }

    [Required(ErrorMessage = "Ange barnets förnamn.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets efternamn.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Eight digits, ÅÅÅÅMMDD. Parsed by <see cref="SwedishDate"/> in the controller.</summary>
    [Required(ErrorMessage = "Ange barnets födelsedatum.")]
    public string BirthDate { get; set; } = string.Empty;
}
