using System.Globalization;
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// What Swish accepts on a payment request. Each rule is a way a payment can be rejected with a
/// 422 that nothing in the booking logic would ever notice, so each is pinned here.
/// </summary>
public class SwishRequestTests
{
    private static readonly Guid Reference = new("3f2504e0-4f89-41d3-9a0c-0305e82c3301");

    [Fact]
    public void Instruction_id_is_32_upper_case_hex_digits_without_hyphens()
    {
        var id = SwishRequest.InstructionId(Reference);

        Assert.Equal("3F2504E04F8941D39A0C0305E82C3301", id);
        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9A-F]{32}$", id);
    }

    [Fact]
    public void Payment_reference_is_the_same_value_and_fits_the_35_alphanumeric_limit()
    {
        var reference = SwishRequest.PaymentReference(Reference);

        Assert.Equal(SwishRequest.InstructionId(Reference), reference);
        Assert.InRange(reference.Length, 1, 35);
        Assert.Matches("^[a-zA-Z0-9-]+$", reference);
    }

    [Theory]
    [InlineData(15_000, "150.00")]
    [InlineData(5, "0.05")]
    [InlineData(25_050, "250.50")]
    [InlineData(100, "1.00")]
    public void Amount_has_two_decimals_and_a_period(int ore, string expected)
        => Assert.Equal(expected, SwishRequest.Amount(ore));

    [Fact]
    public void Amount_ignores_the_thread_culture()
    {
        // sv-SE would write 150,00. Swish rejects a comma.
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            Assert.Equal("150.00", SwishRequest.Amount(15_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Message_for_a_class_names_the_day_and_time_in_swedish()
    {
        var message = SwishRequest.Message("Minitennis", new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.Equal("Träning 12 september 18:00", message);
    }

    [Fact]
    public void Message_without_a_class_is_the_family_upgrade()
        => Assert.Equal("Familjekonto", SwishRequest.Message(null, null));

    [Fact]
    public void Message_for_a_class_with_no_start_is_just_traning()
        => Assert.Equal("Träning", SwishRequest.Message("Minitennis", null));

    [Fact]
    public void Message_never_exceeds_fifty_characters()
    {
        var message = SwishRequest.Message(new string('x', 200), new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.True(message.Length <= 50, $"was {message.Length}");
    }

    [Fact]
    public void Message_contains_only_characters_swish_allows()
    {
        var message = SwishRequest.Message("Tävling – 6–8 år & mer", new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.Matches("^[a-zA-ZåäöÅÄÖ0-9 :;.,?!()\"]", message);
        Assert.DoesNotContain("–", message);
        Assert.DoesNotContain("&", message);
    }

    [Fact]
    public void Callback_identifier_is_32_hex_digits_and_fresh_each_time()
    {
        var first = SwishRequest.CallbackIdentifier();
        var second = SwishRequest.CallbackIdentifier();

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void App_link_carries_the_token_verbatim_and_encodes_the_return_url_once()
    {
        var link = SwishRequest.AppLink(
            "c28a4061470f4af48973bd2a4642b4fa",
            "https://ndstk.se/medlem/betalning?ref=3f2504e0-4f89-41d3-9a0c-0305e82c3301");

        Assert.Equal(
            "swish://paymentrequest?token=c28a4061470f4af48973bd2a4642b4fa"
            + "&callbackurl=https%3A%2F%2Fndstk.se%2Fmedlem%2Fbetalning%3Fref%3D3f2504e0-4f89-41d3-9a0c-0305e82c3301",
            link);
    }
}
