namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Bound from <c>NDSTK:Swish</c>. Everything a real Swish payment needs that is not on the payment
/// row. <see cref="Enabled"/> plus a loadable certificate is what switches the mock off.
/// </summary>
public sealed class SwishOptions
{
    public const string SectionName = "NDSTK:Swish";

    public bool Enabled { get; set; }

    /// <summary>The club's Swish number, ten digits. Never shown to members.</summary>
    public string PayeeAlias { get; set; } = string.Empty;

    /// <summary>Production by default; appsettings.Development.json points at the simulator.</summary>
    public string ApiBaseUrl { get; set; } = "https://cpc.getswish.net/swish-cpcapi/";

    public string QrApiBaseUrl { get; set; } = "https://mpc.getswish.net/qrg-swish/";

    /// <summary>PKCS#12 with the private key and the chain, outside the web root.</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>A secret: appsettings.Secrets.json or an environment variable, never appsettings.json.</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>Alternative to the file: a certificate installed in LocalMachine\My.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>
    /// Development only. When set, replaces the message on every request so the simulator
    /// produces that outcome (RF07, TM01, …). Ignored outside the Development environment.
    /// </summary>
    public string SimulateErrorCode { get; set; } = string.Empty;

    public bool HasCertificateSource
        => !string.IsNullOrWhiteSpace(CertificatePath) || !string.IsNullOrWhiteSpace(CertificateThumbprint);
}

/// <summary>Names of the two HttpClients. The API client carries the certificate; the QR client does not.</summary>
public static class SwishHttpClientNames
{
    public const string Api = "swish";
    public const string Qr = "swish-qr";
}
