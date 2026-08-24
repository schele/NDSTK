using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Services;

/// <summary>
/// The booking feature's configuration, as read from the Settings node.
/// </summary>
/// <remarks>
/// Prices are entered in kronor in the backoffice, because that is what an editor thinks in, and
/// converted to öre exactly once - here, on the way out of the CMS. Nothing downstream ever sees
/// kronor, so there is no second place for a factor of a hundred to go missing.
/// </remarks>
public sealed record MembershipSettings(
    PriceList Prices,
    int ReminderHoursBefore,
    int PaymentHoldMinutes)
{
    /// <summary>
    /// Used when the Settings node has no Medlemskap values yet, which is the case immediately
    /// after the field group is installed. These are the club's agreed prices.
    /// </summary>
    public static MembershipSettings Defaults { get; } = new(
        new PriceList(
            MembershipFeeOre: 150 * 100,
            FamilyFeeOre: 100 * 100,
            FirstClassPriceOre: 100 * 100,
            ClassPriceOre: 200 * 100),
        ReminderHoursBefore: 24,
        // Long enough to open Swish and confirm, short enough that a place is not held for somebody
        // who wandered off. The hold blocks a real member from booking, so erring long is not free.
        PaymentHoldMinutes: 5);
}
