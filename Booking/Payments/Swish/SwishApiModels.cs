using System.Text.Json.Serialization;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>The body of PUT /api/v2/paymentrequests/{instructionUUID}. Property names are Swish's.</summary>
internal sealed record SwishCreateRequest(
    [property: JsonPropertyName("payeePaymentReference")] string PayeePaymentReference,
    [property: JsonPropertyName("callbackUrl")] string CallbackUrl,
    [property: JsonPropertyName("payeeAlias")] string PayeeAlias,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("callbackIdentifier")] string CallbackIdentifier);

/// <summary>The payment request object Swish returns from GET and PATCH, and posts to the callback.</summary>
internal sealed record SwishPaymentRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("paymentReference")] string? PaymentReference,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("datePaid")] DateTime? DatePaid);

/// <summary>One element of the array a 422 carries.</summary>
internal sealed record SwishError(
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage);

/// <summary>The JSON Patch operation that cancels a request. The only one Swish accepts.</summary>
internal sealed record SwishCancelOperation(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("value")] string Value)
{
    public static readonly SwishCancelOperation[] Body = [new("replace", "/status", "cancelled")];
}
