using NDSTK.Booking.Data;

namespace NDSTK.Booking.Payments;

/// <summary>
/// How the club takes money. The booking logic talks to this and nothing else, so the mock and
/// Swish are interchangeable: <c>BookingComposer</c> picks one from configuration.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Recorded on the payment row, so a real payment is distinguishable from a mock.</summary>
    string Name { get; }

    /// <summary>
    /// Creates the request at the provider. Returns what the page needs to hand the member over.
    /// Throws <see cref="PaymentProviderException"/> when the provider refuses or cannot be reached;
    /// the caller leaves the payment untouched so the member can try again.
    /// </summary>
    Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context);

    /// <summary>
    /// Asks the provider what happened. A terminal answer is returned, never thrown. Throws only
    /// when the provider cannot be reached, so a caller can tell "declined" from "unknown".
    /// </summary>
    Task<PaymentOutcome> RetrieveAsync(string providerReference);

    /// <summary>
    /// Withdraws a request the member has not answered. A request that is already final is
    /// reported as its final state rather than as a failure.
    /// </summary>
    Task<PaymentOutcome> CancelAsync(string providerReference);
}
