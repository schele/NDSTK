namespace NDSTK.Booking.Web;

/// <summary>
/// Names of the rate limiting policies the member forms use. Constants rather than literals so the
/// policy registration in Program.cs and the [EnableRateLimiting] attributes cannot drift apart -
/// a typo there fails at request time, not build time.
/// </summary>
public static class BookingRateLimits
{
    /// <summary>
    /// Registration, login and email verification - the endpoints worth brute-forcing, because a
    /// caller who is not yet authenticated is guessing at passwords or tokens. Tight on purpose.
    /// </summary>
    public const string Auth = "ndstk-auth";

    /// <summary>
    /// Booking, cancelling and the payment actions.
    /// </summary>
    /// <remarks>
    /// Deliberately far more generous than <see cref="Auth"/>. These are things a signed-in member
    /// does repeatedly in normal use - book a class, change their mind, pay, book another - and the
    /// caller has already proved who they are. Sharing the strict auth budget meant an ordinary
    /// session ran out of requests and the member got a bare 429, which reads as the site being
    /// broken. The limit here exists to stop a runaway loop, not to police members.
    /// </remarks>
    public const string MemberActions = "ndstk-member-actions";
}
