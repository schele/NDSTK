namespace NDSTK.Booking.Domain;

/// <summary>
/// Decides how many places a class has left. A place is taken by a confirmed booking, or by a
/// pending one whose payment hold has not yet run out.
/// </summary>
public static class Capacity
{
    public static bool HoldsPlace(BookingSnapshot booking, DateTime nowUtc) => booking.Status switch
    {
        BookingStatus.Confirmed => true,
        BookingStatus.Pending => booking.HoldExpiresUtc is null || booking.HoldExpiresUtc > nowUtc,
        _ => false,
    };

    /// <summary>
    /// Never negative: an editor is allowed to lower the capacity below the places already taken,
    /// and the existing bookings stand.
    /// </summary>
    public static int RemainingPlaces(int capacity, IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)
        => Math.Max(0, capacity - bookings.Count(booking => HoldsPlace(booking, nowUtc)));

    /// <summary>
    /// A <em>child</em> may hold at most one live booking per class. A cancelled or expired booking
    /// does not count, so rebooking a class you left is allowed.
    /// </summary>
    /// <remarks>
    /// Keyed on the participant rather than the account: two siblings on one family account are two
    /// participants, and both must fit on the same class. This has to stay in step with the partial
    /// unique index IX_ndstkBooking_OneLivePerParticipantClass, which is the same rule in SQL.
    /// </remarks>
    public static bool HasLiveBooking(
        IEnumerable<BookingSnapshot> bookings, Guid participantKey, DateTime nowUtc)
        => bookings.Any(booking =>
            booking.ParticipantKey == participantKey && HoldsPlace(booking, nowUtc));
}
