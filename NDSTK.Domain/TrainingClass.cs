namespace NDSTK.Booking.Domain;

/// <summary>
/// A single training session, as the editor described it in the backoffice.
/// </summary>
/// <remarks>
/// <paramref name="StartUtc"/> is UTC. The editor types Swedish local time and
/// <see cref="SwedishTime"/> converts it, so nothing downstream has to know about the offset.
/// </remarks>
public sealed record TrainingClass(
    Guid Key,
    string Title,
    string? Description,
    DateTime StartUtc,
    int DurationMinutes,
    int Capacity,
    string? Instructor,
    string? Location)
{
    public DateTime EndUtc => StartUtc.AddMinutes(DurationMinutes);
}
