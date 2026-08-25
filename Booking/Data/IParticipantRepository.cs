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
    /// Brings back a child who was removed from this account, matched on name and birth date.
    /// Returns their key, or null when this is genuinely somebody new.
    /// </summary>
    /// <remarks>
    /// Adding a child back has to restore the same person rather than create a second one, because
    /// the welcome price lives on the participant. A fresh row would arrive with FirstClassUsedUtc
    /// null and hand them a second 100 kr trial class they had already used - and their bookings
    /// would be split across two rows that the club has no way to tell apart.
    ///
    /// Matched in memory rather than in SQL: SQLite's default comparison is case-sensitive, and
    /// COLLATE NOCASE only folds ASCII - it would treat "Åsa" and "åsa" as different people.
    /// </remarks>
    Task<Guid?> TryRestoreAsync(
        Guid memberKey, string firstName, string lastName, DateOnly birthDate);

    /// <summary>
    /// Fills in a child the backfill could only guess at - and only such a child.
    /// </summary>
    /// <remarks>
    /// A child's name and birth date are fixed once they are known. They identify a person on a
    /// class roster and in the club's records, and letting a member rewrite them after the fact
    /// means a coach cannot trust the list in front of them.
    ///
    /// The one exception is a participant created by <c>NdstkParticipantBackfill</c>, whose name
    /// came from an email address and whose birth date is null. That is a placeholder, not a
    /// record, and the member has to be able to correct it or they can never book.
    ///
    /// So the rule is "only while the birth date is still missing", and it lives in the UPDATE's
    /// WHERE clause rather than in a check before it - a forged key changes nothing, and neither
    /// does a second submission after the first has completed.
    /// </remarks>
    Task<bool> TryCompleteAsync(
        Guid participantKey, Guid memberKey, string firstName, string lastName, DateOnly birthDate);

    /// <summary>
    /// Soft delete, so the child's bookings stay readable and last season's class numbers do not
    /// quietly change. Same ownership rule as <see cref="TryCompleteAsync"/>.
    /// </summary>
    Task<bool> TryRemoveAsync(Guid participantKey, Guid memberKey, DateTime nowUtc);

    /// <summary>
    /// Marks this child's welcome price spent. Conditional on the stamp still being null, so two
    /// payments settling at once cannot both believe they were the first.
    /// </summary>
    Task<bool> TryStampFirstClassUsedAsync(Guid participantKey, DateTime nowUtc);
}
