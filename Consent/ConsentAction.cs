namespace NDSTK.Consent;

/// <summary>How a decision was reached. Recorded verbatim in the consent log (build-order stage 7).</summary>
public enum ConsentAction
{
    AcceptAll,
    RejectAll,
    Custom,
    Withdrawn,
}
