namespace NDSTK.Booking.Domain;

/// <summary>
/// Selects the bookings a reminder run should act on. Kept pure so the window boundaries and the
/// no-resend guarantee are testable without a scheduler or a database.
/// </summary>
public static class Reminders
{
    /// <summary>
    /// Confirmed bookings whose class starts within the next <paramref name="hoursBefore"/> hours
    /// and which have not been reminded yet. Stamping each booking as it is sent is what makes a
    /// crashed run safe to repeat.
    /// </summary>
    public static IReadOnlyList<BookingSnapshot> Due(
        IEnumerable<BookingSnapshot> bookings, DateTime nowUtc, int hoursBefore)
    {
        DateTime windowEnd = nowUtc.AddHours(hoursBefore);

        return
        [
            .. bookings
                .Where(booking => booking.Status == BookingStatus.Confirmed)
                .Where(booking => booking.ReminderSentUtc is null)
                .Where(booking => booking.ClassStartUtc > nowUtc && booking.ClassStartUtc <= windowEnd)
                .OrderBy(booking => booking.ClassStartUtc),
        ];
    }

    /// <summary>Pending bookings whose payment hold has run out, so their place can be released.</summary>
    public static IReadOnlyList<BookingSnapshot> ExpiredHolds(
        IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)
        =>
        [
            .. bookings.Where(booking =>
                booking.Status == BookingStatus.Pending
                && booking.HoldExpiresUtc is { } expires
                && expires <= nowUtc),
        ];
}
