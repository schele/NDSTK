namespace NDSTK.Booking.Web;

/// <summary>
/// Names of the rate limiting policies the member forms use. Constants rather than literals so the
/// policy registration in Program.cs and the [EnableRateLimiting] attributes cannot drift apart -
/// a typo there fails at request time, not build time.
/// </summary>
public static class BookingRateLimits
{
    /// <summary>
    /// Registration, login and verification. Per remote IP, so one abusive client cannot lock the
    /// whole club out.
    /// </summary>
    public const string MemberForms = "ndstk-member-forms";
}
