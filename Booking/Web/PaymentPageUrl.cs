using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>
/// Where a member is sent to pay. Resolved from content rather than hard-coded so an editor can
/// rename or move the page, and in one place so the three controllers that send members there
/// cannot drift apart.
/// </summary>
public static class PaymentPageUrl
{
    public static string? For(IPublishedContentQuery contentQuery, IPublishedUrlProvider urlProvider, Guid reference)
    {
        IPublishedContent? page = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("swishPayment"))
            .FirstOrDefault();

        return page is null
            ? null
            : $"{page.Url(urlProvider)}?ref={Uri.EscapeDataString(reference.ToString())}";
    }
}
