using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

/// <summary>
/// A booking credit, issued when a member cancels. A ledger rather than a counter on the member:
/// spending is a conditional UPDATE that cannot double-spend, and the rows are an audit trail.
/// </summary>
[TableName(BookingTables.Credit)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class CreditRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBookingCredit_MemberKey")]
    public Guid MemberKey { get; set; }

    /// <summary>The cancelled booking that produced this credit.</summary>
    [Column(nameof(SourceBookingId))]
    public int SourceBookingId { get; set; }

    /// <summary>Null means unspent. The only link between a credit and the booking it paid for.</summary>
    [Column(nameof(SpentOnBookingId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? SpentOnBookingId { get; set; }

    [Column(nameof(IssuedUtc))]
    public DateTime IssuedUtc { get; set; }

    [Column(nameof(SpentUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? SpentUtc { get; set; }
}
