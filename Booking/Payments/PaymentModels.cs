namespace NDSTK.Booking.Payments;

/// <summary>What the provider needs beyond the payment row itself.</summary>
public sealed record PaymentStartContext(string CallbackUrl, string Message);

/// <summary>
/// What starting a payment produced. <paramref name="Token"/> is the value that opens the Swish
/// app and draws the QR code; the mock has none worth the name.
/// </summary>
public sealed record PaymentStart(string ProviderReference, string? Token, string CallbackIdentifier);

/// <summary>Where a request stands at the provider. Terminal unless <see cref="Status"/> is Created.</summary>
public sealed record PaymentOutcome(
    ProviderStatus Status, string? BankReference, string? ErrorCode, DateTime? PaidUtc)
{
    public bool IsTerminal => Status != ProviderStatus.Created;
}

public enum ProviderStatus
{
    Created,
    Paid,
    Declined,
    Error,
    Cancelled,
}

/// <summary>
/// The provider refused or could not be reached. <see cref="ErrorCode"/> is Swish's code when
/// there was one (a 422), null for a transport failure.
/// </summary>
public sealed class PaymentProviderException(string message, string? errorCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string? ErrorCode { get; } = errorCode;
}
