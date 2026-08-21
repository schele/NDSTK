namespace NDSTK.Consent;

/// <summary>A visitor's recorded consent choice, as carried by the <c>ndstk-consent</c> cookie.</summary>
public sealed record ConsentDecision(
    int PolicyVersion,
    DateTimeOffset DecidedAt,
    IReadOnlySet<ConsentCategory> Granted,
    string ConsentId)
{
    public bool HasGranted(ConsentCategory category)
        => category == ConsentCategory.Necessary || Granted.Contains(category);

    /// <summary>
    /// True when the visitor last decided against an older version of the cookie text, which means
    /// the banner must be shown again with their previous choice pre-selected.
    /// </summary>
    public bool NeedsRePrompt(int currentPolicyVersion) => PolicyVersion < currentPolicyVersion;
}
