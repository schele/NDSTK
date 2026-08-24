namespace NDSTK.Booking.Domain;

/// <summary>
/// What one booking costs, split three ways so the payment page and the backoffice can both show
/// exactly what is being paid for.
/// </summary>
public sealed record BookingQuote(int MembershipDueOre, int FamilyDueOre, int ClassFeeOre)
{
    public int TotalOre => MembershipDueOre + FamilyDueOre + ClassFeeOre;

    /// <summary>False when the total is zero, in which case the Swish step is skipped entirely.</summary>
    public bool RequiresPayment => TotalOre > 0;
}
