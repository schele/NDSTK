using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// What each answer from Swish means to this site. The statuses come from the payment request
/// object; the error codes from the integration guide's table.
/// </summary>
public class SwishOutcomeTests
{
    [Fact]
    public void Created_is_not_terminal_and_keeps_the_payment_pending()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("CREATED", null);

        Assert.False(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Pending, resolution.PaymentStatus);
    }

    [Fact]
    public void Paid_is_terminal_and_paid()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("PAID", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Paid, resolution.PaymentStatus);
    }

    [Fact]
    public void Declined_by_the_member_is_cancelled_and_says_so()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("DECLINED", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Cancelled, resolution.PaymentStatus);
        Assert.Contains("avböjde", resolution.MemberMessage);
    }

    [Fact]
    public void Cancelled_is_cancelled()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("CANCELLED", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Cancelled, resolution.PaymentStatus);
    }

    [Theory]
    [InlineData("RF07")]
    [InlineData("BANKIDCL")]
    [InlineData("FF10")]
    [InlineData("TM01")]
    [InlineData("DS24")]
    [InlineData("BANKIDONGOING")]
    [InlineData("BANKIDUNKN")]
    public void Every_documented_error_code_is_failed_with_its_own_sentence(string code)
    {
        PaymentResolution resolution = SwishOutcome.Resolve("ERROR", code);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Failed, resolution.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(resolution.MemberMessage));
        Assert.NotEqual(SwishOutcome.Resolve("ERROR", "XXXX").MemberMessage, resolution.MemberMessage);
    }

    [Fact]
    public void Timed_out_names_the_cause_so_the_member_knows_to_be_quicker()
        => Assert.Contains("tid", SwishOutcome.Resolve("ERROR", "TM01").MemberMessage);

    [Fact]
    public void Unknown_outcome_tells_the_member_to_check_swish_before_paying_again()
        => Assert.Contains("Swish-appen", SwishOutcome.Resolve("ERROR", "DS24").MemberMessage);

    [Fact]
    public void An_unknown_error_code_is_still_failed_with_a_generic_sentence()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("ERROR", "ZZ99");

        Assert.Equal(PaymentStatus.Failed, resolution.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(resolution.MemberMessage));
    }

    [Fact]
    public void Status_comparison_ignores_case()
        => Assert.Equal(PaymentStatus.Paid, SwishOutcome.Resolve("paid", null).PaymentStatus);

    [Fact]
    public void An_unknown_status_is_not_terminal()
        => Assert.False(SwishOutcome.Resolve("SOMETHING_NEW", null).IsTerminal);
}
