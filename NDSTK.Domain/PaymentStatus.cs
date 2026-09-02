namespace NDSTK.Booking.Domain;

/// <summary>
/// Payment statuses, as readable strings for the same reason as <see cref="BookingStatus"/>: a
/// human opening the SQLite file should be able to read it. In the Domain project so that
/// <see cref="SwishOutcome"/> can name them without a dependency on the web assembly.
/// </summary>
public static class PaymentStatus
{
    /// <summary>Created, waiting for the member to complete the Swish step.</summary>
    public const string Pending = "Pending";

    /// <summary>Completed. The booking it belongs to is confirmed.</summary>
    public const string Paid = "Paid";

    /// <summary>Swish reported an error: declined by the bank, timed out, BankID cancelled.</summary>
    public const string Failed = "Failed";

    /// <summary>The member abandoned it, declined it in the app, or the hold ran out.</summary>
    public const string Cancelled = "Cancelled";
}
