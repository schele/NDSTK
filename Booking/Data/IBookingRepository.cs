using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Data;

/// <summary>
/// All SQL for the booking feature. An interface so the services above it can be reasoned about -
/// and later tested - without a database.
/// </summary>
/// <remarks>
/// The read methods deliberately return the pure <see cref="BookingSnapshot"/> and
/// <see cref="CreditSnapshot"/> records rather than the NPoco POCOs. That keeps the rules in
/// NDSTK.Domain free of any persistence type, and it means a query change cannot quietly alter what
/// the rules see.
/// </remarks>
public interface IBookingRepository
{
    /// <summary>
    /// Every booking for the given classes, whatever its status - the capacity rule needs the
    /// cancelled and expired ones too, in order to discount them.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>>> GetBookingsByClassAsync(
        IReadOnlyCollection<Guid> classKeys);

    /// <summary>One member's bookings, newest class first.</summary>
    Task<IReadOnlyList<BookingSnapshot>> GetBookingsForMemberAsync(Guid memberKey);

    /// <summary>One member's credits, spent and unspent.</summary>
    Task<IReadOnlyList<CreditSnapshot>> GetCreditsForMemberAsync(Guid memberKey);
}
