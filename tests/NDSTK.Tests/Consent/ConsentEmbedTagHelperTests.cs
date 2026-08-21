using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NDSTK.Consent;
using NDSTK.Consent.TagHelpers;
using Umbraco.Cms.Core.Dictionary;

namespace NDSTK.Tests.Consent;

public class ConsentEmbedTagHelperTests
{
    private sealed class StubDictionary : ICultureDictionary, ICultureDictionaryFactory
    {
        public string this[string key] => $"[{key}]";

        public CultureInfo Culture => CultureInfo.InvariantCulture;

        public IDictionary<string, string> GetChildren(string key) => new Dictionary<string, string>();

        public ICultureDictionary CreateDictionary() => this;

        public ICultureDictionary CreateDictionary(CultureInfo culture) => this;
    }

    private static TagHelperContext Context() => new(
        new TagHelperAttributeList(),
        new Dictionary<object, object>(),
        Guid.NewGuid().ToString());

    private static TagHelperOutput Output() => new(
        "consent-embed",
        new TagHelperAttributeList(),
        (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static ConsentEmbedTagHelper Helper(IConsentState consent) =>
        new(consent, new StubDictionary())
        {
            Category = ConsentCategory.Marketing,
            Src = "https://www.youtube-nocookie.com/embed/abc",
            Title = "Klubbfilm",
        };

    [Fact]
    public void Renders_an_iframe_when_granted()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState(ConsentCategory.Marketing)).Process(Context(), output);

        var html = output.Content.GetContent();
        Assert.Equal("div", output.TagName);
        Assert.Contains("<iframe", html);
        Assert.Contains("https://www.youtube-nocookie.com/embed/abc", html);
        Assert.Contains("title=\"Klubbfilm\"", html);
    }

    [Fact]
    public void Renders_a_placeholder_with_no_iframe_when_not_granted()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState()).Process(Context(), output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("data-consent-open", html);
        Assert.Contains("[Cookies.Embed.Blocked.Body]", html);
        Assert.Contains("[Cookies.Embed.Blocked.Button]", html);
    }

    [Fact]
    public void The_placeholder_never_leaks_the_embed_url()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState()).Process(Context(), output);

        Assert.DoesNotContain("youtube-nocookie.com", output.Content.GetContent());
    }

    [Fact]
    public void Escapes_a_hostile_title()
    {
        TagHelperOutput output = Output();
        ConsentEmbedTagHelper helper = Helper(new FakeConsentState(ConsentCategory.Marketing));
        helper.Title = "\"><script>alert(1)</script>";

        helper.Process(Context(), output);

        Assert.DoesNotContain("<script>alert(1)</script>", output.Content.GetContent());
    }
}
