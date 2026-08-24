using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

/// <summary>One member's place on one class.</summary>
[TableName(BookingTables.Booking)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class BookingRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(ClassKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_ClassKey")]
    public Guid ClassKey { get; set; }

    /// <summary>
    /// Denormalised from the class node so the reminder query is one indexed range scan and never
    /// touches the published cache. Resynced when an editor republishes the class.
    /// </summary>
    [Column(nameof(ClassStartUtc))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_ClassStartUtc")]
    public DateTime ClassStartUtc { get; set; }

    /// <summary>One of the <see cref="Domain.BookingStatus"/> constants.</summary>
    [Column(nameof(Status))]
    [Length(20)]
    public string Status { get; set; } = Domain.BookingStatus.Pending;

    [Column(nameof(PaymentId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? PaymentId { get; set; }

    /// <summary>
    /// Set while the booking is pending. Once it passes, the place is released - otherwise a
    /// member who closed the payment tab would hold a place for ever.
    /// </summary>
    [Column(nameof(HoldExpiresUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? HoldExpiresUtc { get; set; }

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    [Column(nameof(ConfirmedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ConfirmedUtc { get; set; }

    [Column(nameof(CancelledUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CancelledUtc { get; set; }

    /// <summary>Stamped as each reminder is sent, which is what makes a repeated run safe.</summary>
    [Column(nameof(ReminderSentUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReminderSentUtc { get; set; }
}
