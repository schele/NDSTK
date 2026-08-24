namespace NDSTK.Booking.Payments;

/// <summary>
/// Stands in for Swish until the real integration exists.
/// </summary>
/// <remarks>
/// Deliberately does nothing beyond naming itself. All the mock's behaviour - the styled page, the
/// simulate buttons - lives in the payment page and controller, because that is what a real
/// provider would replace: the club's own pages would stay, only the redirect target and the
/// callback would change.
/// </remarks>
public sealed class SwishMockPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "SwishMock";

    public string Name => ProviderName;

    public bool RequiresRedirect => true;
}
