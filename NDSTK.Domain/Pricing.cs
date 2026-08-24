namespace NDSTK.Booking.Domain;

/// <summary>
/// The whole pricing rule, as a pure function. Deliberately free of Umbraco and the database so
/// every combination of membership, discount and credit is cheap to test.
/// </summary>
public static class Pricing
{
    public static BookingQuote Quote(MemberState member, PriceList prices, bool useCredit, DateOnly today)
    {
        int membershipDueOre = IsMembershipValid(member, today) ? 0 : prices.MembershipFeeOre;

        // A credit is worth one place, so it clears the class fee but never the membership fee.
        // It also leaves the welcome price unspent - see the discount flag, which only moves when
        // a payment that actually charged it completes.
        int classFeeOre = useCredit
            ? 0
            : member.FirstClassDiscountUsed
                ? prices.ClassPriceOre
                : prices.FirstClassPriceOre;

        return new BookingQuote(membershipDueOre, classFeeOre);
    }

    /// <summary>The paid-until day is inclusive: a membership expiring today is still valid today.</summary>
    public static bool IsMembershipValid(MemberState member, DateOnly today)
        => member.MembershipPaidUntil is { } paidUntil && paidUntil >= today;
}
