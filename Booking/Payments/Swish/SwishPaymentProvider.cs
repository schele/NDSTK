using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Swish Commerce over the v2 API: create with PUT, retrieve with GET, cancel with PATCH.
/// </summary>
/// <remarks>
/// The client named <see cref="SwishHttpClientNames.Api"/> carries the merchant certificate; see
/// SwishHttpClients. Nothing here logs the token or the callback identifier. The instruction id
/// is logged, because it is what support matches against Swish's own logs.
/// </remarks>
public sealed class SwishPaymentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SwishOptions> options,
    IHostEnvironment environment,
    ILogger<SwishPaymentProvider> logger) : IPaymentProvider
{
    public const string ProviderName = "Swish";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public async Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context)
    {
        SwishOptions swish = options.Value;
        var instructionId = SwishRequest.InstructionId(payment.Reference);
        var callbackIdentifier = SwishRequest.CallbackIdentifier();

        // Against the simulator, an error code in the message is how an outcome is chosen.
        // Read only in Development so no production setting can ever change what members see.
        var message = environment.IsDevelopment() && !string.IsNullOrWhiteSpace(swish.SimulateErrorCode)
            ? swish.SimulateErrorCode.Trim()
            : context.Message;

        var body = new SwishCreateRequest(
            SwishRequest.PaymentReference(payment.Reference),
            context.CallbackUrl,
            swish.PayeeAlias,
            SwishRequest.Amount(payment.AmountOre),
            "SEK",
            message,
            callbackIdentifier);

        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        HttpResponseMessage response;
        try
        {
            response = await client.PutAsJsonAsync($"api/v2/paymentrequests/{instructionId}", body, Json);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Swish could not be reached to create request {InstructionId}.", instructionId);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw await RefusalAsync(response, $"create request {instructionId}");
            }

            var token = response.Headers.TryGetValues("PaymentRequestToken", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrEmpty(token))
            {
                // Swish returns the token only for m-commerce, which is the only kind we send.
                // Its absence means the request is not what we think it is.
                logger.LogError("Swish created request {InstructionId} without a PaymentRequestToken.", instructionId);
                throw new PaymentProviderException("Swish returned no payment request token.");
            }

            logger.LogInformation("Swish request {InstructionId} created.", instructionId);
            return new PaymentStart(instructionId, token, callbackIdentifier);
        }
    }

    public async Task<PaymentOutcome> RetrieveAsync(string providerReference)
    {
        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"api/v1/paymentrequests/{providerReference}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Swish could not be reached to retrieve request {InstructionId}.", providerReference);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode is false)
            {
                throw await RefusalAsync(response, $"retrieve request {providerReference}");
            }

            SwishPaymentRequest? request = await response.Content.ReadFromJsonAsync<SwishPaymentRequest>(Json);
            return ToOutcome(request, providerReference);
        }
    }

    public async Task<PaymentOutcome> CancelAsync(string providerReference)
    {
        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        using var content = JsonContent.Create(SwishCancelOperation.Body, options: Json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");

        HttpResponseMessage response;
        try
        {
            response = await client.PatchAsync($"api/v1/paymentrequests/{providerReference}", content);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Swish could not be reached to cancel request {InstructionId}.", providerReference);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // RP07: already final. Not a failure - the answer is whatever it became.
                logger.LogInformation(
                    "Swish request {InstructionId} could not be cancelled; it is already final.", providerReference);
                return await RetrieveAsync(providerReference);
            }

            if (response.IsSuccessStatusCode is false)
            {
                throw await RefusalAsync(response, $"cancel request {providerReference}");
            }

            SwishPaymentRequest? request = await response.Content.ReadFromJsonAsync<SwishPaymentRequest>(Json);
            logger.LogInformation("Swish request {InstructionId} cancelled.", providerReference);
            return ToOutcome(request, providerReference);
        }
    }

    private static PaymentOutcome ToOutcome(SwishPaymentRequest? request, string providerReference)
    {
        if (request?.Status is null)
        {
            throw new PaymentProviderException($"Swish returned no status for request {providerReference}.");
        }

        ProviderStatus status = request.Status.ToUpperInvariant() switch
        {
            SwishOutcome.Paid => ProviderStatus.Paid,
            SwishOutcome.Declined => ProviderStatus.Declined,
            SwishOutcome.Error => ProviderStatus.Error,
            SwishOutcome.Cancelled => ProviderStatus.Cancelled,
            _ => ProviderStatus.Created,
        };

        return new PaymentOutcome(status, request.PaymentReference, request.ErrorCode, request.DatePaid);
    }

    /// <summary>
    /// Turns a non-success response into an exception that carries Swish's error code when the
    /// body is the 422 error array, and the HTTP status otherwise.
    /// </summary>
    private async Task<PaymentProviderException> RefusalAsync(HttpResponseMessage response, string what)
    {
        string? code = null;
        string? detail = null;

        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden)
        {
            try
            {
                SwishError[]? errors = await response.Content.ReadFromJsonAsync<SwishError[]>(Json);
                SwishError? first = errors?.FirstOrDefault();
                code = first?.ErrorCode;
                detail = first?.ErrorMessage;
            }
            catch (JsonException)
            {
                // A body that is not the documented array. The status code is still informative.
            }
        }

        logger.LogError(
            "Swish refused to {What}: HTTP {Status}{Code}{Detail}.",
            what, (int)response.StatusCode,
            code is null ? string.Empty : $" {code}",
            detail is null ? string.Empty : $" ({detail})");

        return new PaymentProviderException(
            $"Swish refused to {what} with HTTP {(int)response.StatusCode}.", code);
    }
}
