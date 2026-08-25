namespace NDSTK.Booking.Domain;

/// <summary>
/// The whole pricing rule, as a pure function. Deliberately free of Umbraco and the database so
/// every combination of membership, family account, discount and credit is cheap to test.
/// </summary>
public static class Pricing
{
    /// <summary>
    /// How long a paid membership runs. Shared so that the code which sets the expiry and the code
    /// which works backwards from it to find the start of the current year cannot disagree.
    /// </summary>
    public const int MembershipDays = 365;

    public static BookingQuote Quote(
        MemberState member, ParticipantState participant, PriceList prices,
        bool useCredit, DateOnly today)
    {
        var valid = IsMembershipValid(member, today);

        // The family supplement rides along with the annual fee and is never charged on its own
        // here. Buying it mid-year is a separate purchase - see FamilyUpgradeQuote - which
        // deliberately does not move the expiry date.
        int membershipDueOre = valid ? 0 : prices.MembershipFeeOre;
        int familyDueOre = valid || member.IsFamilyAccount is false ? 0 : prices.FamilyFeeOre;

        // A credit is worth one place, so it clears the class fee but never the membership or
        // family fee. It also leaves the welcome price unspent - see FirstClassUsed, which only
        // moves when a payment that actually charged it completes.
        int classFeeOre = useCredit
            ? 0
            : participant.FirstClassUsed
                ? prices.ClassPriceOre
                : prices.FirstClassPriceOre;

        return new BookingQuote(membershipDueOre, familyDueOre, classFeeOre);
    }

    /// <summary>
    /// Upgrading a paid-up account to a family account, mid-year, as a purchase of its own.
    /// </summary>
    /// <remarks>
    /// Deliberately does not extend the membership. If it did, the supplement would be cheaper than
    /// the annual fee and no member would ever pay the annual fee a second time. The trade is that
    /// upgrading a month before expiry buys only that month - which is visible to the member at the
    /// time, and self-correcting, since they renew at the family price next time.
    /// </remarks>
    public static BookingQuote FamilyUpgradeQuote(PriceList prices)
        => new(MembershipDueOre: 0, FamilyDueOre: prices.FamilyFeeOre, ClassFeeOre: 0);

    /// <summary>The paid-until day is inclusive: a membership expiring today is still valid today.</summary>
    public static bool IsMembershipValid(MemberState member, DateOnly today)
        => member.MembershipPaidUntil is { } paidUntil && paidUntil >= today;
}
