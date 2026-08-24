namespace NDSTK.Booking.Data;

/// <summary>
/// All SQL for participants. Separate from <see cref="IBookingRepository"/>, which is about places
/// on classes rather than about the people who take them.
/// </summary>
public interface IParticipantRepository
{
    /// <summary>One account's children, oldest first. Removed ones are left out.</summary>
    Task<IReadOnlyList<ParticipantRecord>> GetForMemberAsync(Guid memberKey);

    /// <summary>
    /// The same, but including removed children. For anything that renders history: a booking made
    /// by a child who has since left still needs a name against it.
    /// </summary>
    Task<IReadOnlyList<ParticipantRecord>> GetAllForMemberAsync(Guid memberKey);

    Task<ParticipantRecord?> GetAsync(Guid participantKey);

    Task<Guid> CreateAsync(
        Guid memberKey, string firstName, string lastName, DateOnly birthDate, DateTime nowUtc);

    /// <summary>
    /// Returns false when the participant is not this member's, so a forged key in a form edits
    /// nothing. The ownership check is a condition of the UPDATE rather than a read before it.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid participantKey, Guid memberKey, string firstName, string lastName, DateOnly birthDate);

    /// <summary>
    /// Soft delete, so the child's bookings stay readable and last season's class numbers do not
    /// quietly change. Same ownership rule as <see cref="TryUpdateAsync"/>.
    /// </summary>
    Task<bool> TryRemoveAsync(Guid participantKey, Guid memberKey, DateTime nowUtc);

    /// <summary>
    /// Marks this child's welcome price spent. Conditional on the stamp still being null, so two
    /// payments settling at once cannot both believe they were the first.
    /// </summary>
    Task<bool> TryStampFirstClassUsedAsync(Guid participantKey, DateTime nowUtc);
}
