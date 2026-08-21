using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

internal sealed class FakeConsentState(params ConsentCategory[] granted) : IConsentState
{
    private readonly HashSet<ConsentCategory> _granted = granted.ToHashSet();

    public ConsentDecision? Decision => new(1, DateTimeOffset.UtcNow, _granted, "test");

    public bool NeedsDecision { get; init; }

    public bool HasGranted(ConsentCategory category)
        => category == ConsentCategory.Necessary || (NeedsDecision is false && _granted.Contains(category));
}
