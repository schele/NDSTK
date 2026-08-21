namespace NDSTK.Consent;

/// <summary>Body of <c>POST /api/consent</c>. Every field is untrusted and validated server-side.</summary>
public sealed class ConsentRequest
{
    public string[]? Categories { get; set; }

    public string? Action { get; set; }

    public string? Culture { get; set; }
}
