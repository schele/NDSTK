using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NDSTK.Booking.Payments.Swish;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Decides which provider takes money: Swish when it is enabled and the certificate loads,
/// the mock otherwise. Resolved once, as a singleton.
/// </summary>
public static class PaymentProviderFactory
{
    public static IPaymentProvider Create(IServiceProvider services)
    {
        SwishOptions options = services.GetRequiredService<IOptions<SwishOptions>>().Value;
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PaymentProviderFactory));

        if (options.Enabled is false)
        {
            logger.LogWarning("Payment provider: SwishMock. NDSTK:Swish:Enabled is false; no money is taken.");
            return new SwishMockPaymentProvider();
        }

        if (options.HasCertificateSource is false)
        {
            logger.LogWarning("Payment provider: SwishMock. Swish is enabled but no certificate is configured.");
            return new SwishMockPaymentProvider();
        }

        if (string.IsNullOrWhiteSpace(options.PayeeAlias))
        {
            logger.LogWarning("Payment provider: SwishMock. Swish is enabled but NDSTK:Swish:PayeeAlias is empty.");
            return new SwishMockPaymentProvider();
        }

        if (services.GetRequiredService<SwishCertificateLoader>().Load() is null)
        {
            // The loader has already logged why.
            logger.LogWarning("Payment provider: SwishMock. The Swish certificate did not load.");
            return new SwishMockPaymentProvider();
        }

        logger.LogInformation("Payment provider: Swish, against {ApiBaseUrl}.", options.ApiBaseUrl);
        return ActivatorUtilities.CreateInstance<SwishPaymentProvider>(services);
    }
}
