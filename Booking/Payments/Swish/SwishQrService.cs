using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Turns a payment request token into the QR image Swish's own generator draws for it, as a PNG.
/// Cached per payment for ten minutes, which outlives any request Swish will still honour, so a
/// page that polls and reloads does not fetch the same image again and again.
/// </summary>
public sealed class SwishQrService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<SwishQrService> logger)
{
    /// <summary>
    /// Swish's generator takes only these four. It accepts a colour parameter and silently ignores
    /// it - their design specification fixes the code as a 45 degree purple-to-red gradient, and
    /// allows only black and white as an alternative - so the page tints the ground behind it
    /// instead, which is what <c>transparent</c> is for.
    /// </summary>
    private sealed record QrRequest(string Token, string Format, int Size, bool Transparent);

    public async Task<byte[]?> GetImageAsync(Guid paymentReference, string token)
    {
        var key = $"swish-qr:{paymentReference:N}";
        if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
        {
            return cached;
        }

        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Qr);

        try
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "api/v1/commerce", new QrRequest(token, "png", 300, Transparent: true));

            if (response.IsSuccessStatusCode is false)
            {
                logger.LogWarning(
                    "The Swish QR service answered HTTP {Status} for payment {Reference}.",
                    (int)response.StatusCode, paymentReference);
                return null;
            }

            var image = await response.Content.ReadAsByteArrayAsync();
            cache.Set(key, image, TimeSpan.FromMinutes(10));
            return image;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "The Swish QR service could not be reached for payment {Reference}.", paymentReference);
            return null;
        }
    }
}
