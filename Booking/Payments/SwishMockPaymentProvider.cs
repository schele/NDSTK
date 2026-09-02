using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Stands in for Swish when no certificate is configured.
/// </summary>
/// <remarks>
/// Starting a payment invents a reference and a token so the page reaches its "started" state.
/// Retrieving always answers Created: the mock has no app for anyone to approve in, so the two
/// simulate buttons settle the payment directly through <c>BookingService</c> instead, exactly as
/// they always have. That keeps this class free of any database dependency, which matters
/// because the provider is a singleton and the repository is scoped.
/// </remarks>
public sealed class SwishMockPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "SwishMock";

    public string Name => ProviderName;

    public Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context)
        => Task.FromResult(new PaymentStart(
            SwishRequest.InstructionId(payment.Reference),
            Token: "mock",
            SwishRequest.CallbackIdentifier()));

    public Task<PaymentOutcome> RetrieveAsync(string providerReference)
        => Task.FromResult(new PaymentOutcome(ProviderStatus.Created, null, null, null));

    public Task<PaymentOutcome> CancelAsync(string providerReference)
        => Task.FromResult(new PaymentOutcome(ProviderStatus.Cancelled, null, null, null));
}
