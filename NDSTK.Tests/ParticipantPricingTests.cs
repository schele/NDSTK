using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class ParticipantPricingTests
{
    private static readonly PriceList Prices = new(
        MembershipFeeOre: 15_000,
        FamilyFeeOre: 10_000,
        FirstClassPriceOre: 10_000,
        ClassPriceOre: 20_000);

    private static readonly DateOnly Today = new(2026, 8, 25);

    private static MemberState Solo(DateOnly? paidUntil) => new(paidUntil, IsFamilyAccount: false);
    private static MemberState Family(DateOnly? paidUntil) => new(paidUntil, IsFamilyAccount: true);
    private static ParticipantState NewChild => new(FirstClassUsed: false);
    private static ParticipantState OldChild => new(FirstClassUsed: true);

    [Fact]
    public void Lapsed_family_account_pays_the_membership_fee_and_the_family_supplement()
    {
        BookingQuote quote = Pricing.Quote(Family(null), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
        Assert.Equal(45_000, quote.TotalOre);
    }

    [Fact]
    public void Lapsed_solo_account_is_not_charged_the_family_supplement()
    {
        BookingQuote quote = Pricing.Quote(Solo(null), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
    }

    [Fact]
    public void Paid_up_family_account_pays_neither_fee_again()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void The_welcome_price_is_per_child_not_per_account()
    {
        MemberState paidUp = Family(new DateOnly(2027, 1, 1));

        // Two different children of the same account, each on their first class.
        BookingQuote elsa = Pricing.Quote(paidUp, NewChild, Prices, useCredit: false, Today);
        BookingQuote nils = Pricing.Quote(paidUp, NewChild, Prices, useCredit: false, Today);

        Assert.Equal(10_000, elsa.ClassFeeOre);
        Assert.Equal(10_000, nils.ClassFeeOre);
    }

    [Fact]
    public void A_child_who_has_used_their_welcome_price_pays_full_price()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void A_credit_clears_the_class_fee_but_never_the_membership_or_family_fee()
    {
        BookingQuote quote = Pricing.Quote(Family(null), NewChild, Prices, useCredit: true, Today);

        Assert.Equal(0, quote.ClassFeeOre);
        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.True(quote.RequiresPayment);
    }

    [Fact]
    public void A_paid_up_member_spending_a_credit_owes_nothing_at_all()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), NewChild, Prices, useCredit: true, Today);

        Assert.Equal(0, quote.TotalOre);
        Assert.False(quote.RequiresPayment);
    }

    [Fact]
    public void The_family_upgrade_is_quoted_on_its_own_with_no_class_or_membership_fee()
    {
        BookingQuote quote = Pricing.FamilyUpgradeQuote(Prices);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.Equal(0, quote.ClassFeeOre);
        Assert.Equal(10_000, quote.TotalOre);
    }

    [Fact]
    public void Membership_expiring_today_is_still_valid_for_a_family_account()
    {
        BookingQuote quote = Pricing.Quote(Family(Today), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
    }
}
