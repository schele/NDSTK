namespace NDSTK.Booking.Domain;

/// <summary>
/// Booking statuses, as strings rather than an enum so the value stored in SQLite is readable
/// when someone opens the database by hand.
/// </summary>
public static class BookingStatus
{
    /// <summary>Holds a place while the member is on the payment page.</summary>
    public const string Pending = "Pending";

    /// <summary>Paid, or covered by a credit. The only status that receives reminders.</summary>
    public const string Confirmed = "Confirmed";

    /// <summary>Cancelled by the member. Produces a credit, never a refund.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>The hold ran out, or the member abandoned the payment.</summary>
    public const string Expired = "Expired";
}
