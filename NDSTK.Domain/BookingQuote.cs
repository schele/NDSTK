namespace NDSTK.Booking.Domain;

/// <summary>
/// What one booking costs, split so the payment page can show the member why.
/// </summary>
public sealed record BookingQuote(int MembershipDueOre, int ClassFeeOre)
{
    public int TotalOre => MembershipDueOre + ClassFeeOre;

    /// <summary>False when the total is zero, in which case the Swish step is skipped entirely.</summary>
    public bool RequiresPayment => TotalOre > 0;
}
