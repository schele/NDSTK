using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace NDSTK.Consent;

/// <summary>
/// Reads and caches the consent cookie for the lifetime of one request. Registered scoped, so the
/// cookie is parsed at most once per request no matter how many tag helpers ask.
/// </summary>
internal sealed class ConsentState(
    IHttpContextAccessor httpContextAccessor,
    IOptions<ConsentOptions> options) : IConsentState
{
    private bool _resolved;
    private ConsentDecision? _decision;

    public ConsentDecision? Decision
    {
        get
        {
            if (_resolved)
            {
                return _decision;
            }

            _resolved = true;
            var raw = httpContextAccessor.HttpContext?.Request.Cookies[options.Value.CookieName];
            _decision = ConsentCookieCodec.Decode(raw);
            return _decision;
        }
    }

    public bool NeedsDecision
        => Decision is null || Decision.NeedsRePrompt(options.Value.PolicyVersion);

    public bool HasGranted(ConsentCategory category)
    {
        if (category == ConsentCategory.Necessary)
        {
            return true;
        }

        // A decision made against older cookie text grants nothing until it is renewed.
        return NeedsDecision is false && Decision?.HasGranted(category) is true;
    }
}
