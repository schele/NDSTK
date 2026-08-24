using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class PricingTests
{
    private static readonly PriceList Prices = new(
        MembershipFeeOre: 15_000,
        FirstClassPriceOre: 10_000,
        ClassPriceOre: 20_000);

    private static readonly DateOnly Today = new(2026, 8, 24);

    private static MemberState Member(DateOnly? paidUntil, bool discountUsed = false)
        => new(paidUntil, discountUsed);

    [Fact]
    public void Brand_new_member_pays_membership_plus_the_discounted_first_class()
    {
        BookingQuote quote = Pricing.Quote(Member(null), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.ClassFeeOre);
        Assert.Equal(25_000, quote.TotalOre);
        Assert.True(quote.RequiresPayment);
    }

    [Fact]
    public void Paid_up_member_pays_only_the_full_class_price()
    {
        BookingQuote quote = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void Membership_expiring_today_is_still_valid()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today, discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
    }

    [Fact]
    public void Membership_that_expired_yesterday_is_charged_again()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today.AddDays(-1), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
    }

    // A lapsed member renews on their next booking - but the welcome price is once per account for
    // life, so renewal is the full class price. 150 + 200, never 150 + 100.
    [Fact]
    public void A_lapsed_member_renewing_pays_the_fee_plus_the_full_class_price()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today.AddDays(-1), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
        Assert.Equal(35_000, quote.TotalOre);
    }

    // The one case where a lapsed member does get the welcome price: they never had it. Someone who
    // registered, never booked, and let a comped membership lapse is still a first-timer.
    [Fact]
    public void A_lapsed_member_who_never_used_the_discount_still_gets_it()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today.AddDays(-1), discountUsed: false), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.ClassFeeOre);
        Assert.Equal(25_000, quote.TotalOre);
    }

    // Expiry is a cliff, not a taper: a year lapsed costs the same as a day lapsed.
    [Fact]
    public void How_long_ago_the_membership_lapsed_does_not_change_the_price()
    {
        BookingQuote yesterday = Pricing.Quote(
            Member(Today.AddDays(-1), discountUsed: true), Prices, useCredit: false, Today);
        BookingQuote longAgo = Pricing.Quote(
            Member(Today.AddDays(-900), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(yesterday.TotalOre, longAgo.TotalOre);
    }

    [Fact]
    public void Paid_up_member_spending_a_credit_owes_nothing_and_skips_payment()
    {
        BookingQuote quote = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: true), Prices, useCredit: true, Today);

        Assert.Equal(0, quote.TotalOre);
        Assert.False(quote.RequiresPayment);
    }

    [Fact]
    public void Lapsed_member_spending_a_credit_still_pays_the_membership_fee()
    {
        BookingQuote quote = Pricing.Quote(
            Member(null, discountUsed: true), Prices, useCredit: true, Today);

        Assert.Equal(15_000, quote.TotalOre);
        Assert.Equal(0, quote.ClassFeeOre);
        Assert.True(quote.RequiresPayment);
    }

    // The welcome price must survive being spent on a credit booking, otherwise cancelling your
    // first class silently costs you the discount as well as the money.
    [Fact]
    public void Spending_a_credit_does_not_consume_the_first_class_discount()
    {
        BookingQuote credited = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: false), Prices, useCredit: true, Today);
        Assert.Equal(0, credited.ClassFeeOre);

        BookingQuote next = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: false), Prices, useCredit: false, Today);
        Assert.Equal(10_000, next.ClassFeeOre);
    }
}
