using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Payments.Swish;
using NDSTK.Booking.Services;

namespace NDSTK.Booking.Web;

/// <summary>
/// Where Swish posts the outcome of a payment request.
/// </summary>
/// <remarks>
/// The body is read for one thing: which request it is about. Everything else about the payment
/// is then fetched from Swish over mTLS by <see cref="BookingService.ReconcileAsync"/>, so a forged
/// body cannot settle anything. The callbackIdentifier header, which only Swish and this server
/// know, must match the stored value first; a mismatch is logged and otherwise ignored.
///
/// Answers 200 for everything except a malformed body. A non-200 buys ten retries of the same
/// request, which would be useless for an unknown id and merely noisy for a duplicate.
///
/// Anonymous by design; attribute-routed like <c>MemberAdminController</c>, so it needs no
/// routing changes.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route(SwishCallbackUrl.Path)]
public sealed partial class SwishCallbackController(
    IBookingRepository repository,
    BookingService bookings,
    ILogger<SwishCallbackController> logger) : ControllerBase
{
    /// <summary>The one field of Swish's payment request object this endpoint reads.</summary>
    public sealed record CallbackBody([property: JsonPropertyName("id")] string? Id);

    [HttpPost]
    [Consumes("application/json")]
    [EnableRateLimiting(BookingRateLimits.Callback)]
    public async Task<IActionResult> Receive([FromBody] CallbackBody body)
    {
        // Swish's instruction id is 32 hexadecimal digits and nothing else. Checking the shape here
        // means an anonymous caller cannot put newlines - or anything else - into a log line, and
        // turns scanner traffic into a refusal rather than a database round trip.
        var id = body.Id?.Trim();
        if (id is null || InstructionId().IsMatch(id) is false)
        {
            return BadRequest();
        }

        PaymentRecord? payment = await repository.GetPaymentByProviderReferenceAsync(id);
        var presented = Request.Headers["callbackIdentifier"].FirstOrDefault();

        if (payment is null || Matches(payment.CallbackIdentifier, presented) is false)
        {
            logger.LogWarning(
                "A Swish callback for request {InstructionId} was not accepted: {Reason}.",
                id, payment is null ? "unknown request" : "callback identifier mismatch");
            return Ok();
        }

        try
        {
            await bookings.ReconcileAsync(payment, DateTime.UtcNow);
            logger.LogInformation("Swish callback for request {InstructionId} reconciled.", id);
        }
        catch (PaymentProviderException exception)
        {
            // Swish just told us something and now cannot be asked about it. The page's poll or
            // the job will ask again; a 500 here would only make Swish repeat the callback.
            logger.LogWarning(exception, "Reconciling after the callback for {InstructionId} failed.", id);
        }

        return Ok();
    }

    /// <summary>Constant-time comparison. Different lengths compare unequal without leaking which.</summary>
    private static bool Matches(string? stored, string? presented)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(stored);
        var b = Encoding.UTF8.GetBytes(presented);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex InstructionId();
}
