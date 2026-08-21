using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Umbraco.Cms.Core.Dictionary;

namespace NDSTK.Consent.TagHelpers;

/// <summary>
/// Renders a third-party embed, or a placeholder inviting the visitor to grant the category it needs.
/// </summary>
/// <remarks>
/// The placeholder deliberately does not contain the embed URL in any form. Emitting it — even hidden,
/// even in a data attribute — is how "blocked" embeds end up firing requests anyway.
/// </remarks>
[HtmlTargetElement("consent-embed", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ConsentEmbedTagHelper(
    IConsentState consent,
    ICultureDictionaryFactory cultureDictionaryFactory) : TagHelper
{
    [HtmlAttributeName("category")]
    public ConsentCategory Category { get; set; } = ConsentCategory.Marketing;

    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (consent.HasGranted(Category))
        {
            output.Attributes.SetAttribute("class", "consent-embed");
            output.Content.SetHtmlContent(
                $"""<iframe src="{encoder.Encode(Src ?? string.Empty)}" title="{encoder.Encode(Title ?? string.Empty)}" loading="lazy" allowfullscreen></iframe>""");
            return;
        }

        ICultureDictionary dictionary = cultureDictionaryFactory.CreateDictionary();
        var body = dictionary["Cookies.Embed.Blocked.Body"];
        var button = dictionary["Cookies.Embed.Blocked.Button"];

        output.Attributes.SetAttribute("class", "consent-embed consent-embed--blocked");
        output.Attributes.SetAttribute("data-consent-category", ConsentCategories.ToWireName(Category));
        output.Content.SetHtmlContent(
            $"""
            <p>{encoder.Encode(body)}</p>
            <button type="button" class="btn-primary" data-consent-open>{encoder.Encode(button)}</button>
            """);
    }
}
