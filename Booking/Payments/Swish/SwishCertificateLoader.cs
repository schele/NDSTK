using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Loads the merchant certificate once. Null, with an error in the log, when it cannot be loaded -
/// the factory then falls back to the mock, and says so.
/// </summary>
/// <remarks>
/// Not EphemeralKeySet. SChannel cannot present an ephemeral private key as a TLS client
/// certificate, and both development and production are Windows. MachineKeySet puts the key in
/// the machine container, which the IIS application pool identity can read.
/// </remarks>
public sealed class SwishCertificateLoader(
    IOptions<SwishOptions> options,
    ILogger<SwishCertificateLoader> logger)
{
    private readonly Lazy<X509Certificate2?> certificate = new(() => Load(options.Value, logger));

    public X509Certificate2? Load() => certificate.Value;

    private static X509Certificate2? Load(SwishOptions swish, ILogger logger)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(swish.CertificateThumbprint))
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);

                X509Certificate2? found = store.Certificates
                    .Find(X509FindType.FindByThumbprint, swish.CertificateThumbprint.Trim(), validOnly: false)
                    .FirstOrDefault();

                if (found is null)
                {
                    logger.LogError("No certificate with the configured thumbprint is in LocalMachine\\My.");
                    return null;
                }

                if (found.HasPrivateKey is false)
                {
                    logger.LogError("The Swish certificate in the store has no private key.");
                    return null;
                }

                return found;
            }

            if (!string.IsNullOrWhiteSpace(swish.CertificatePath))
            {
                X509Certificate2 loaded = X509CertificateLoader.LoadPkcs12FromFile(
                    swish.CertificatePath,
                    swish.CertificatePassword,
                    X509KeyStorageFlags.MachineKeySet);

                if (loaded.HasPrivateKey is false)
                {
                    logger.LogError("The Swish certificate file has no private key.");
                    return null;
                }

                return loaded;
            }

            return null;
        }
        catch (Exception exception)
        {
            // The path and the reason are logged; the password never is.
            logger.LogError(exception, "The Swish certificate could not be loaded from {Path}.", swish.CertificatePath);
            return null;
        }
    }
}
