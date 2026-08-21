using Microsoft.AspNetCore.Razor.TagHelpers;

namespace NDSTK.Consent.TagHelpers;

/// <summary>
/// Emits a <c>&lt;script&gt;</c> only when the visitor has granted the given category.
/// </summary>
/// <remarks>
/// This is the primary gating mechanism and the reason the "no consenting cookies before a choice"
/// guarantee holds without a race: when consent is absent the tag never reaches the browser at all,
/// so there is no window in which it could execute.
/// </remarks>
[HtmlTargetElement("consent-script")]
public sealed class ConsentScriptTagHelper(IConsentState consent) : TagHelper
{
    [HtmlAttributeName("category")]
    public ConsentCategory Category { get; set; } = ConsentCategory.Marketing;

    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    [HtmlAttributeName("async")]
    public bool Async { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (consent.HasGranted(Category) is false)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (string.IsNullOrWhiteSpace(Src) is false)
        {
            output.Attributes.SetAttribute("src", Src);
        }

        if (Async)
        {
            output.Attributes.SetAttribute(
                new TagHelperAttribute("async", null, HtmlAttributeValueStyle.Minimized));
        }
    }
}
