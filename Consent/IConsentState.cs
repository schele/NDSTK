namespace NDSTK.Consent;

/// <summary>Request-scoped view of the current visitor's consent.</summary>
public interface IConsentState
{
    /// <summary>The decoded decision, or null when there is no usable cookie.</summary>
    ConsentDecision? Decision { get; }

    /// <summary>True when the banner must be shown: no decision, or one made against older text.</summary>
    bool NeedsDecision { get; }

    /// <summary>
    /// True only when the visitor has actively granted this category under the current policy version.
    /// <see cref="ConsentCategory.Necessary"/> is always true.
    /// </summary>
    bool HasGranted(ConsentCategory category);
}
