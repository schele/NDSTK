using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CreditTests
{
    private static readonly Guid Member = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void No_credits_means_nothing_to_spend()
    {
        Assert.Equal(0, Credits.CountUnspent([]));
        Assert.Null(Credits.NextSpendable([]));
    }

    [Fact]
    public void An_unspent_credit_is_counted_and_offered()
    {
        CreditSnapshot[] credits = [new(1, Member, null)];

        Assert.Equal(1, Credits.CountUnspent(credits));
        Assert.Equal(1, Credits.NextSpendable(credits)!.Id);
    }

    [Fact]
    public void A_spent_credit_is_neither_counted_nor_offered()
    {
        CreditSnapshot[] credits = [new(1, Member, SpentOnBookingId: 99)];

        Assert.Equal(0, Credits.CountUnspent(credits));
        Assert.Null(Credits.NextSpendable(credits));
    }

    // Oldest first, so a member's credits are used in the order they were earned.
    [Fact]
    public void The_oldest_unspent_credit_is_offered_first()
    {
        CreditSnapshot[] credits = [new(7, Member, null), new(3, Member, null), new(5, Member, 12)];

        Assert.Equal(3, Credits.NextSpendable(credits)!.Id);
        Assert.Equal(2, Credits.CountUnspent(credits));
    }
}
