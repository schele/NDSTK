using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>Registers the two named clients and the certificate loader.</summary>
public static class SwishHttpClients
{
    public static IUmbracoBuilder AddSwishHttpClients(this IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<SwishCertificateLoader>();

        builder.Services.AddHttpClient(SwishHttpClientNames.Api, (services, client) =>
            {
                // A trailing slash is required: the provider's relative request paths
                // (api/v2/paymentrequests/...) resolve against BaseAddress, and without the slash
                // the last segment of a configured URL (e.g. /swish-cpcapi) is silently dropped.
                client.BaseAddress = new Uri(services.GetRequiredService<IOptions<SwishOptions>>().Value.ApiBaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var handler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                };

                X509Certificate2? certificate = services.GetRequiredService<SwishCertificateLoader>().Load();
                if (certificate is not null)
                {
                    handler.ClientCertificates.Add(certificate);
                }

                return handler;
            });

        // The QR generator is public: no certificate, shorter timeout, an image in reply.
        builder.Services.AddHttpClient(SwishHttpClientNames.Qr, (services, client) =>
        {
            // Same trailing-slash normalisation as the API client above, and for the same reason.
            client.BaseAddress = new Uri(services.GetRequiredService<IOptions<SwishOptions>>().Value.QrApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return builder;
    }
}
