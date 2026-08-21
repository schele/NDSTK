using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentStateTests
{
    private static IConsentState StateFor(string? cookieValue, int policyVersion = 1)
    {
        var options = new ConsentOptions { PolicyVersion = policyVersion };
        var httpContext = new DefaultHttpContext();

        if (cookieValue is not null)
        {
            // A real browser echoes back exactly the percent-encoded text the Set-Cookie response
            // gave it. ConsentCookieCodec.Encode returns plain JSON (task-3 fix round 1: the cookie
            // layer, not the codec, does the one encoding pass), so this helper must apply that
            // encoding itself to build a realistic raw Cookie header — otherwise characters like
            // '"' and ',' break RFC 6265 cookie-value grammar before ConsentState ever sees them.
            httpContext.Request.Headers.Cookie = $"{options.CookieName}={Uri.EscapeDataString(cookieValue)}";
        }

        return new ConsentState(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(options));
    }

    private static string CookieFor(int version, params ConsentCategory[] granted)
        => ConsentCookieCodec.Encode(
            new ConsentDecision(version, DateTimeOffset.UtcNow, granted.ToHashSet(), "abc123"));

    [Fact]
    public void Needs_a_decision_when_no_cookie_is_present()
    {
        IConsentState state = StateFor(null);

        Assert.True(state.NeedsDecision);
        Assert.Null(state.Decision);
    }

    [Fact]
    public void Necessary_is_granted_even_without_a_decision()
        => Assert.True(StateFor(null).HasGranted(ConsentCategory.Necessary));

    [Fact]
    public void Non_necessary_is_denied_without_a_decision()
    {
        IConsentState state = StateFor(null);

        Assert.False(state.HasGranted(ConsentCategory.Statistics));
        Assert.False(state.HasGranted(ConsentCategory.Marketing));
        Assert.False(state.HasGranted(ConsentCategory.Preferences));
    }

    [Fact]
    public void Reads_granted_categories_from_the_cookie()
    {
        IConsentState state = StateFor(CookieFor(1, ConsentCategory.Statistics));

        Assert.False(state.NeedsDecision);
        Assert.True(state.HasGranted(ConsentCategory.Statistics));
        Assert.False(state.HasGranted(ConsentCategory.Marketing));
    }

    [Fact]
    public void An_outdated_policy_version_denies_everything_and_reprompts()
    {
        IConsentState state = StateFor(CookieFor(1, ConsentCategory.Statistics), policyVersion: 2);

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
        Assert.True(state.HasGranted(ConsentCategory.Necessary));
        Assert.NotNull(state.Decision);
    }

    [Fact]
    public void A_corrupt_cookie_is_treated_as_no_decision()
    {
        IConsentState state = StateFor("garbage");

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
    }

    [Fact]
    public void Survives_having_no_http_context()
    {
        IConsentState state = new ConsentState(
            new HttpContextAccessor { HttpContext = null },
            Options.Create(new ConsentOptions()));

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
    }
}
