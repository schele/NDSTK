namespace NDSTK.Booking.Domain;

/// <summary>
/// When a booking may still be cancelled.
/// </summary>
/// <remarks>
/// The club does not want late cancellations: a place given up an hour before the class cannot
/// realistically be filled by anybody else, so it costs the club a coached slot and the member
/// nothing. Cancelling closes a configurable number of hours before the start.
/// </remarks>
public static class Cancellation
{
    /// <summary>
    /// Whether the window is still open for a class starting at <paramref name="classStartUtc"/>.
    /// </summary>
    /// <remarks>
    /// Strictly greater than, so a booking exactly on the deadline is already closed. The boundary
    /// has to fall one way or the other, and closing early is the direction that matches the point
    /// of having a deadline at all.
    /// </remarks>
    public static bool IsOpen(DateTime classStartUtc, DateTime nowUtc, int deadlineHours)
        => classStartUtc - nowUtc > TimeSpan.FromHours(deadlineHours);

    /// <summary>
    /// The moment a class must start after for its booking to still be cancellable - the form the
    /// rule takes in a SQL WHERE clause, where "now" cannot be added to.
    /// </summary>
    public static DateTime EarliestCancellableStart(DateTime nowUtc, int deadlineHours)
        => nowUtc.AddHours(deadlineHours);
}
