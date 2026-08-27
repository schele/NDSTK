using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Gets an API-user token, then posts the scan's declarations to the site's merge endpoint.
/// </summary>
/// <remarks>
/// A failure here is reported and swallowed rather than thrown: the scan's findings are worth
/// having even when the write-back cannot happen, and a violation must still fail the run on its
/// own merits. <see cref="MergeAsync"/> returns null in that case and the report says so.
/// </remarks>
public sealed class ManagementApiClient(ScanOptions options)
{
    private const string TokenPath = "/umbraco/management/api/v1/security/back-office/token";
    private const string MergePath = "/umbraco/management/api/v1/cookie-scan/merge";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<MergeOutcome?> MergeAsync(IReadOnlyList<CookieDeclarationCandidate> candidates)
    {
        using HttpClient http = CreateClient();

        try
        {
            string token = await TokenAsync(http);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var request = new
            {
                declarations = candidates.Select(candidate => new
                {
                    name = candidate.Name,
                    provider = candidate.Provider,
                    category = candidate.Category,
                    purpose = candidate.Purpose,
                    duration = candidate.Duration,
                    storageType = candidate.StorageType,
                }),
                dryRun = options.DryRun,
            };

            using HttpResponseMessage response = await http.PostAsJsonAsync(MergePath, request, Json);

            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode is false)
            {
                Console.Error.WriteLine(
                    $"  The merge endpoint returned HTTP {(int)response.StatusCode}: {body}");

                return null;
            }

            MergeResponse? parsed = JsonSerializer.Deserialize<MergeResponse>(body, Json);

            if (parsed is null)
            {
                Console.Error.WriteLine("  The merge endpoint returned a body that could not be read.");

                return null;
            }

            return new MergeOutcome(
                parsed.Added ?? [],
                parsed.AlreadyDeclared ?? [],
                parsed.DeclaredButNotFound ?? [],
                parsed.PolicyPageKey,
                parsed.Saved);
        }
        // JsonException included: a 2xx response with a non-JSON body - an HTML login or error
        // page is the realistic case - makes the Deserialize call above throw one, and without
        // this filter it would escape MergeAsync into the scanner's top-level catch, which exits
        // the process WITHOUT ever writing the report. That loses the whole scan's findings, not
        // just the write-back - the one thing this method's own remarks promise cannot happen.
        // TokenAsync's deserialize is covered by the same filter: it runs inside this same try,
        // called from the line above.
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            Console.Error.WriteLine($"  Write-back failed: {error.Message}");

            return null;
        }
    }

    private async Task<string> TokenAsync(HttpClient http)
    {
        // Form-encoded, as the OAuth client-credentials grant specifies. If the endpoint turns out
        // to want JSON, that is spec risk 2 and this is the line to change.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
        });

        using HttpResponseMessage response = await http.PostAsync(TokenPath, form);

        string body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode is false)
        {
            throw new InvalidOperationException(
                $"Could not get a token (HTTP {(int)response.StatusCode}). Check the client id, and "
                + $"that {ScanOptions.SecretVariable} holds the matching secret. Response: {body}");
        }

        TokenResponse? token = JsonSerializer.Deserialize<TokenResponse>(body, Json);

        return string.IsNullOrWhiteSpace(token?.AccessToken)
            ? throw new InvalidOperationException("The token response contained no access_token.")
            : token.AccessToken;
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler();

        // Only for a loopback target, and only so a scan of a local site behind a dev certificate
        // works without the operator having to trust it first. Deliberately not extended to a real
        // host: silently accepting any certificate when talking to production, while sending a
        // client secret, would be indefensible.
        if (options.Target.IsLoopback)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        return new HttpClient(handler)
        {
            BaseAddress = options.Target,
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record MergeResponse(
        IReadOnlyList<string>? Added,
        IReadOnlyList<string>? AlreadyDeclared,
        IReadOnlyList<string>? DeclaredButNotFound,
        Guid PolicyPageKey,
        bool Saved);
}
