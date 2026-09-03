using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// The merchant certificate and the CA certificates that vouch for it.
/// </summary>
/// <remarks>
/// The chain is not decoration. Schannel refuses to send a client certificate it cannot build a
/// chain for, so a leaf on its own fails the TLS handshake against Swish with nothing but
/// "HandshakeFailure" - while OpenSSL negotiates the very same certificate happily, which makes it
/// look like Swish's fault. Measured against the Merchant Swish Simulator: leaf alone fails, leaf
/// plus these intermediates succeeds, and nothing else was needed.
/// </remarks>
public sealed record SwishCertificate(X509Certificate2 Leaf, X509Certificate2Collection Intermediates);

/// <summary>
/// Loads the merchant certificate once. Null, with an error in the log, when it cannot be loaded -
/// the factory then falls back to the mock, and says so.
/// </summary>
/// <remarks>
/// Not EphemeralKeySet. SChannel cannot present an ephemeral private key as a TLS client
/// certificate, and both development and production are Windows. MachineKeySet puts the key in
/// the machine container, which the IIS application pool identity can read.
/// </remarks>
public sealed partial class SwishCertificateLoader(
    IOptions<SwishOptions> options,
    ILogger<SwishCertificateLoader> logger)
{
    private readonly Lazy<SwishCertificate?> certificate = new(() => Load(options.Value, logger));

    public SwishCertificate? Load() => certificate.Value;

    private static SwishCertificate? Load(SwishOptions swish, ILogger logger)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(swish.CertificateThumbprint))
            {
                // The Windows certificate dialog prepends a left-to-right mark and separates byte
                // pairs with spaces when a thumbprint is copied from it. Neither is whitespace, so
                // Trim does not help, and the lookup silently finds nothing.
                var thumbprint = NonHex().Replace(swish.CertificateThumbprint, string.Empty);

                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);

                X509Certificate2? found = store.Certificates
                    .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                    .FirstOrDefault();

                if (found is null)
                {
                    // The thumbprint is not a secret, and naming it is the difference between a
                    // five-minute fix and an afternoon.
                    logger.LogError(
                        "No certificate with thumbprint {Thumbprint} is in LocalMachine\\My.", thumbprint);
                    return null;
                }

                if (found.HasPrivateKey is false)
                {
                    logger.LogError("The Swish certificate in the store has no private key.");
                    return null;
                }

                WarnIfExpiring(found, logger);

                // A certificate in the store gets its chain from the store, which is why this is
                // the route the go-live checklist prefers: importing it puts the CA certificates
                // where Schannel already looks.
                return Chained(found, [], logger);
            }

            if (!string.IsNullOrWhiteSpace(swish.CertificatePath))
            {
                // The whole file, not just the leaf. A Swish merchant export carries the CA
                // certificates up to the Swish root alongside the leaf, and LoadPkcs12FromFile
                // would hand back only the leaf and drop exactly what the handshake needs.
                X509Certificate2Collection all = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                    swish.CertificatePath,
                    swish.CertificatePassword,
                    X509KeyStorageFlags.MachineKeySet);

                X509Certificate2? leaf = all.FirstOrDefault(candidate => candidate.HasPrivateKey);
                if (leaf is null)
                {
                    logger.LogError("The Swish certificate file has no private key.");
                    return null;
                }

                WarnIfExpiring(leaf, logger);

                var intermediates = new X509Certificate2Collection();
                foreach (X509Certificate2 other in all)
                {
                    if (!ReferenceEquals(other, leaf) && other.Thumbprint != leaf.Thumbprint)
                    {
                        intermediates.Add(other);
                    }
                }

                return Chained(leaf, intermediates, logger);
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

    /// <summary>
    /// Pairs the leaf with its chain, and says so at boot when there is no chain to be had from
    /// either the file or the machine's own stores.
    /// </summary>
    /// <remarks>
    /// This is the one failure worth naming before it happens. Without a buildable chain Schannel
    /// declines to send the certificate at all, and every payment then fails with a bare
    /// "HandshakeFailure" that says nothing about certificates - it took six wrong guesses to
    /// diagnose once. A PartialChain here is not fatal on its own: the intermediates travel with
    /// the request, and the Swish root is not in the Windows store even on a working setup, so the
    /// warning fires only when there is nothing to send.
    /// </remarks>
    private static SwishCertificate Chained(
        X509Certificate2 leaf, X509Certificate2Collection intermediates, ILogger logger)
    {
        if (intermediates.Count > 0)
        {
            logger.LogInformation(
                "The Swish certificate came with {Count} CA certificate(s) for its chain.", intermediates.Count);

            return new SwishCertificate(leaf, intermediates);
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (chain.Build(leaf) is false)
        {
            logger.LogWarning(
                "The Swish certificate brought no CA chain and Windows cannot build one for it "
                + "({Status}). Schannel will not send a client certificate it cannot chain, so "
                + "payments would fail with a bare TLS handshake error. Export the certificate "
                + "together with its CA certificates, or import those into the machine's stores.",
                string.Join(", ", chain.ChainStatus.Select(status => status.Status)));
        }

        return new SwishCertificate(leaf, intermediates);
    }

    /// <summary>
    /// A Swish merchant certificate is valid two years. When it lapses the TLS handshake simply
    /// fails, members are told Swish cannot be reached, and nothing in the log names the cause - so
    /// the expiry is stated at every boot and shouted about while there is still time to renew it.
    /// </summary>
    private static void WarnIfExpiring(X509Certificate2 certificate, ILogger logger)
    {
        DateTime expires = certificate.NotAfter.ToUniversalTime();
        var daysLeft = (int)(expires - DateTime.UtcNow).TotalDays;

        if (daysLeft <= 0)
        {
            logger.LogError(
                "The Swish certificate expired on {Expires:u}. Payments will fail until it is renewed.",
                expires);
        }
        else if (daysLeft <= 30)
        {
            logger.LogWarning(
                "The Swish certificate expires on {Expires:u}, in {DaysLeft} day(s). Renew it at "
                + "portal.swish.nu before then.", expires, daysLeft);
        }
        else
        {
            logger.LogInformation("The Swish certificate is valid until {Expires:u}.", expires);
        }
    }

    [GeneratedRegex("[^0-9a-fA-F]")]
    private static partial Regex NonHex();
}
