using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentModeScriptTests
{
    [Fact]
    public void Defaults_deny_every_signal()
    {
        var script = ConsentModeScript.Defaults();

        Assert.Contains("'ad_storage':'denied'", script);
        Assert.Contains("'ad_user_data':'denied'", script);
        Assert.Contains("'ad_personalization':'denied'", script);
        Assert.Contains("'analytics_storage':'denied'", script);
        Assert.Contains("'functionality_storage':'denied'", script);
        Assert.Contains("'personalization_storage':'denied'", script);
        Assert.Contains("'wait_for_update':500", script);
        Assert.DoesNotContain("granted", script);
    }

    [Fact]
    public void Statistics_grants_only_analytics_storage()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Statistics));

        Assert.Contains("'analytics_storage':'granted'", script);
        Assert.Contains("'ad_storage':'denied'", script);
        Assert.Contains("'functionality_storage':'denied'", script);
    }

    [Fact]
    public void Marketing_grants_the_three_ad_signals()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Marketing));

        Assert.Contains("'ad_storage':'granted'", script);
        Assert.Contains("'ad_user_data':'granted'", script);
        Assert.Contains("'ad_personalization':'granted'", script);
        Assert.Contains("'analytics_storage':'denied'", script);
    }

    [Fact]
    public void Preferences_grants_functionality_and_personalization()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Preferences));

        Assert.Contains("'functionality_storage':'granted'", script);
        Assert.Contains("'personalization_storage':'granted'", script);
        Assert.Contains("'ad_storage':'denied'", script);
    }

    [Fact]
    public void Nothing_granted_denies_everything()
    {
        var script = ConsentModeScript.Update(new FakeConsentState());

        Assert.DoesNotContain("granted", script);
    }
}
