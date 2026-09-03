using System.Globalization;
using System.Text.RegularExpressions;

namespace NDSTK.Booking.Domain;

/// <summary>
/// The values a Swish payment request is built from, formatted the way Swish validates them.
/// </summary>
/// <remarks>
/// Every method here corresponds to a 422 Swish would otherwise answer with: FF08 for a bad
/// reference, PA02 for a bad amount, RP02 for a bad message. None of that is visible to the
/// booking rules, so the formatting is pinned by tests instead.
/// </remarks>
public static partial class SwishRequest
{
    /// <summary>Swish caps the message at fifty characters.</summary>
    public const int MessageMaxLength = 50;

    private static readonly CultureInfo Swedish = new("sv-SE");

    /// <summary>
    /// The identifier under which the request is stored at Swish: 32 upper-case hexadecimal
    /// digits, no hyphens. The payment's own Guid, so the two can always be matched up.
    /// </summary>
    public static string InstructionId(Guid reference)
        => reference.ToString("N").ToUpperInvariant();

    /// <summary>
    /// The merchant reference Swish echoes back. Same value as the instruction id: 32
    /// alphanumerics fit the 1–35 limit and the allowed alphabet.
    /// </summary>
    public static string PaymentReference(Guid reference) => InstructionId(reference);

    /// <summary>"150.00". Invariant culture: a Swedish thread would write a comma.</summary>
    public static string Amount(int ore)
        => (ore / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the member sees in their Swish history. Built from the class rather than typed, so
    /// no title can smuggle in a character Swish rejects - or, against the simulator, an error
    /// code.
    /// </summary>
    public static string Message(string? classTitle, DateTime? classStartSwedish)
    {
        if (classTitle is null)
        {
            return "Familjekonto";
        }

        var text = classStartSwedish is { } start
            ? $"Träning {start.ToString("d MMMM HH:mm", Swedish)}"
            : "Träning";

        return Sanitise(text);
    }

    /// <summary>A fresh value per request. Never logged; it is what authenticates the callback.</summary>
    public static string CallbackIdentifier() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// The URL that opens the Swish app with the request preloaded. The return URL is encoded
    /// exactly once; the app decodes it once before opening it.
    /// </summary>
    public static string AppLink(string token, string returnUrl)
        => $"swish://paymentrequest?token={token}&callbackurl={Uri.EscapeDataString(returnUrl)}";

    private static string Sanitise(string text)
    {
        var allowed = Disallowed().Replace(text, string.Empty);
        var collapsed = Whitespace().Replace(allowed, " ").Trim();

        return collapsed.Length <= MessageMaxLength
            ? collapsed
            : collapsed[..MessageMaxLength].TrimEnd();
    }

    [GeneratedRegex("[^a-zA-ZåäöÅÄÖ0-9 :;.,?!()\"]")]
    private static partial Regex Disallowed();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
