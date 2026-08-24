namespace NDSTK.Booking.Data;

/// <summary>
/// Payment statuses, as readable strings for the same reason as
/// <see cref="Domain.BookingStatus"/>: a human opening the SQLite file should be able to read it.
/// </summary>
public static class PaymentStatus
{
    /// <summary>Created, waiting for the member to complete the Swish step.</summary>
    public const string Pending = "Pending";

    /// <summary>Completed. The booking it belongs to is confirmed.</summary>
    public const string Paid = "Paid";

    /// <summary>The provider rejected it.</summary>
    public const string Failed = "Failed";

    /// <summary>The member abandoned it, or the hold ran out.</summary>
    public const string Cancelled = "Cancelled";
}
