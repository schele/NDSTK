namespace NDSTK.Booking.Domain;

/// <summary>
/// A single training session, as the editor described it in the backoffice.
/// </summary>
/// <remarks>
/// <paramref name="StartUtc"/> is UTC. The editor types Swedish local time and
/// <see cref="SwedishTime"/> converts it, so nothing downstream has to know about the offset.
///
/// <paramref name="MapUrl"/> comes from the club's address on the Settings node rather than from
/// anything on the class. It rides along here so the views and the reminder mail all get it from
/// the class they are already holding - see <see cref="MapLink"/>.
/// </remarks>
public sealed record TrainingClass(
    Guid Key,
    string Title,
    string? Description,
    DateTime StartUtc,
    int DurationMinutes,
    int Capacity,
    ClassInstructor? Instructor,
    string? Location,
    string? MapUrl = null)
{
    public DateTime EndUtc => StartUtc.AddMinutes(DurationMinutes);
}
