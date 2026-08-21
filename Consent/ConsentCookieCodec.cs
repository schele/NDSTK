using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NDSTK.Consent;

/// <summary>
/// Serialises a <see cref="ConsentDecision"/> to and from the cookie's compact JSON form.
/// </summary>
/// <remarks>
/// Decoding is deliberately total: any malformed, truncated or hand-edited value decodes to
/// <c>null</c>, which the rest of the system treats as "no decision yet". The cookie is not a
/// security boundary — the worst a visitor can do is forge their own consent — so it is not signed.
/// </remarks>
public static class ConsentCookieCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(ConsentDecision decision)
    {
        var dto = new ConsentCookieDto
        {
            Version = decision.PolicyVersion,
            DecidedAt = decision.DecidedAt.ToUniversalTime(),
            Categories = decision.Granted
                .Where(category => category != ConsentCategory.Necessary)
                .Select(ConsentCategories.ToWireName)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ConsentId = decision.ConsentId,
        };

        return Uri.EscapeDataString(JsonSerializer.Serialize(dto, SerializerOptions));
    }

    public static ConsentDecision? Decode(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var json = Uri.UnescapeDataString(cookieValue);
            ConsentCookieDto? dto = JsonSerializer.Deserialize<ConsentCookieDto>(json, SerializerOptions);

            if (dto is null || dto.Version <= 0 || string.IsNullOrWhiteSpace(dto.ConsentId))
            {
                return null;
            }

            var granted = new HashSet<ConsentCategory>();
            foreach (var name in dto.Categories ?? [])
            {
                if (ConsentCategories.TryParse(name, out ConsentCategory category)
                    && category != ConsentCategory.Necessary)
                {
                    granted.Add(category);
                }
            }

            return new ConsentDecision(dto.Version, dto.DecidedAt, granted, dto.ConsentId);
        }
        catch (Exception exception) when (exception is JsonException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A random 128-bit, URL-safe id linking the cookie to its consent-log row.</summary>
    public static string NewConsentId()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class ConsentCookieDto
    {
        [JsonPropertyName("v")] public int Version { get; set; }

        [JsonPropertyName("t")] public DateTimeOffset DecidedAt { get; set; }

        [JsonPropertyName("c")] public string[]? Categories { get; set; }

        [JsonPropertyName("id")] public string? ConsentId { get; set; }
    }
}
