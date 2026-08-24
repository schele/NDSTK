using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

/// <summary>
/// One person who attends classes. The account holder is a guardian; this is the child.
/// </summary>
/// <remarks>
/// A table rather than an Umbraco member, because Umbraco requires a unique email per member: three
/// siblings would mean three synthesised addresses and three Identity logins to disable. A table
/// rather than content nodes, because these are minors' names and birth dates and they have no
/// business in the published cache.
/// </remarks>
[TableName(BookingTables.Participant)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class ParticipantRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>What bookings reference. A key rather than the id, so it is safe to put in a form.</summary>
    [Column(nameof(Key))]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_ndstkParticipant_Key")]
    public Guid Key { get; set; }

    /// <summary>The guardian's account.</summary>
    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkParticipant_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(FirstName))]
    [Length(100)]
    public string FirstName { get; set; } = string.Empty;

    [Column(nameof(LastName))]
    [Length(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Null only on rows the backfill created for members who registered before participants
    /// existed. The portal refuses to book for such a child until it is filled in: inventing a
    /// birth date would be worse than asking for it once.
    /// </summary>
    [Column(nameof(BirthDate))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// The welcome price, per child. Moved off the member so a second child on a family account
    /// does not inherit their sibling's spent discount.
    /// </summary>
    [Column(nameof(FirstClassUsedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? FirstClassUsedUtc { get; set; }

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Soft delete. Removing the row outright would orphan the child's bookings and quietly change
    /// last season's class numbers.
    /// </summary>
    [Column(nameof(RemovedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? RemovedUtc { get; set; }
}
