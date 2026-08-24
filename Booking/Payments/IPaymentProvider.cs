namespace NDSTK.Booking.Payments;

/// <summary>
/// How the club takes money. One interface so the mocked Swish flow can be replaced by the real
/// one without touching the booking logic: a second implementation and one DI line.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Recorded on the payment row, so a real payment is distinguishable from a mock.</summary>
    string Name { get; }

    /// <summary>
    /// True when the member has to be sent somewhere to pay. A real provider would return a
    /// redirect or a QR payload here; the mock sends them to a page on this site.
    /// </summary>
    bool RequiresRedirect { get; }
}
