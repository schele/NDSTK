using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Data;

/// <summary>One attempt to take money for one booking.</summary>
[TableName(BookingTables.Payment)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class PaymentRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>The value that appears in the payment page URL. Unique, and not guessable.</summary>
    [Column(nameof(Reference))]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_ndstkPayment_Reference")]
    public Guid Reference { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkPayment_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(BookingId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? BookingId { get; set; }

    /// <summary>Total charged, in öre. Integer because SQLite maps decimal to REAL.</summary>
    [Column(nameof(AmountOre))]
    public int AmountOre { get; set; }

    /// <summary>The membership part of the total, kept so the page can show the member why.</summary>
    [Column(nameof(MembershipFeeOre))]
    public int MembershipFeeOre { get; set; }

    /// <summary>
    /// The family supplement part of the total. Kept separate so the backoffice can answer "how
    /// much, and for what" without inferring anything from the total.
    /// </summary>
    [Column(nameof(FamilyFeeOre))]
    public int FamilyFeeOre { get; set; }

    [Column(nameof(ClassFeeOre))]
    public int ClassFeeOre { get; set; }

    /// <summary>One of the <see cref="PaymentStatus"/> constants.</summary>
    [Column(nameof(Status))]
    [Length(20)]
    public string Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Which provider handled it, so a real Swish payment is distinguishable from a mock.</summary>
    [Column(nameof(Provider))]
    [Length(50)]
    public string Provider { get; set; } = string.Empty;

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    [Column(nameof(CompletedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CompletedUtc { get; set; }
}
