using Microsoft.AspNetCore.Razor.TagHelpers;
using NDSTK.Consent;
using NDSTK.Consent.TagHelpers;

namespace NDSTK.Tests.Consent;

public class ConsentScriptTagHelperTests
{
    private static TagHelperContext Context() => new(
        new TagHelperAttributeList(),
        new Dictionary<object, object>(),
        Guid.NewGuid().ToString());

    private static TagHelperOutput Output() => new(
        "consent-script",
        new TagHelperAttributeList(),
        (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    [Fact]
    public void Emits_nothing_at_all_when_the_category_is_not_granted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState())
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.True(output.IsContentModified);
        Assert.Null(output.TagName);
        Assert.Empty(output.Content.GetContent());
    }

    [Fact]
    public void Emits_a_script_tag_when_granted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState(ConsentCategory.Statistics))
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
            Async = true,
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Equal("script", output.TagName);
        Assert.Equal(TagMode.StartTagAndEndTag, output.TagMode);
        Assert.Equal("https://example.test/a.js", output.Attributes["src"].Value);
        Assert.True(output.Attributes.ContainsName("async"));
    }

    [Fact]
    public void Omits_async_when_not_requested()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState(ConsentCategory.Marketing))
        {
            Category = ConsentCategory.Marketing,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.False(output.Attributes.ContainsName("async"));
    }

    [Fact]
    public void Necessary_scripts_are_always_emitted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState())
        {
            Category = ConsentCategory.Necessary,
            Src = "/static/js/consent.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Equal("script", output.TagName);
    }

    [Fact]
    public void A_stale_decision_suppresses_the_script()
    {
        var helper = new ConsentScriptTagHelper(
            new FakeConsentState(ConsentCategory.Statistics) { NeedsDecision = true })
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Null(output.TagName);
    }
}
