namespace NDSTK.Booking.Domain;

/// <summary>
/// What a Swish status means here: the status to store, whether anything can still change,
/// and the sentence the member reads.
/// </summary>
public sealed record PaymentResolution(bool IsTerminal, string PaymentStatus, string MemberMessage);

/// <summary>
/// Maps the status and error code on a Swish payment request object to this site's terms.
/// </summary>
/// <remarks>
/// The error codes are the ones the integration guide lists for a payment request. An unknown
/// code is still a failure - Swish said ERROR - it just gets a sentence that does not guess.
/// </remarks>
public static class SwishOutcome
{
    public const string Created = "CREATED";
    public const string Paid = "PAID";
    public const string Declined = "DECLINED";
    public const string Error = "ERROR";
    public const string Cancelled = "CANCELLED";

    private const string GenericFailure =
        "Betalningen gick inte igenom. Platsen är inte bokad. Försök igen, eller kontakta oss på "
        + "info@ndstk.se om det upprepas.";

    public static PaymentResolution Resolve(string status, string? errorCode)
    {
        switch (status.ToUpperInvariant())
        {
            case Paid:
                return new PaymentResolution(true, Domain.PaymentStatus.Paid, "Klart! Betalningen är genomförd.");

            case Declined:
                return new PaymentResolution(
                    true, Domain.PaymentStatus.Cancelled, "Du avböjde betalningen i Swish. Platsen är inte bokad.");

            case Cancelled:
                return new PaymentResolution(
                    true, Domain.PaymentStatus.Cancelled, "Betalningen avbröts. Platsen är inte bokad.");

            case Error:
                return new PaymentResolution(true, Domain.PaymentStatus.Failed, FailureMessage(errorCode));

            default:
                // CREATED, and anything Swish adds later: nothing has been decided.
                return new PaymentResolution(false, Domain.PaymentStatus.Pending, "Väntar på Swish.");
        }
    }

    private static string FailureMessage(string? errorCode) => errorCode?.ToUpperInvariant() switch
    {
        "RF07" => "Banken nekade betalningen. Platsen är inte bokad. Kontrollera din Swish-gräns med banken.",
        "BANKIDCL" => "Signeringen med BankID avbröts, så betalningen genomfördes inte. Platsen är inte bokad.",
        "BANKIDONGOING" => "BankID var upptaget med något annat. Avsluta det och försök igen.",
        "BANKIDUNKN" => "BankID kunde inte godkänna betalningen. Platsen är inte bokad.",
        "FF10" => "Ett fel uppstod hos banken. Platsen är inte bokad. Försök igen om en liten stund.",
        "TM01" => "Betalningen hann inte godkännas i tid. Platsen är inte bokad. Boka igen och öppna Swish direkt.",
        "DS24" => "Swish fick inget svar från banken, så det är oklart om pengarna drogs. Kontrollera i "
                  + "Swish-appen innan du försöker igen, och kontakta oss på info@ndstk.se om du blivit debiterad.",
        _ => GenericFailure,
    };
}
