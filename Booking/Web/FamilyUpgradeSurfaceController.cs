using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Filters;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>
/// Sells the family account upgrade: a purchase of its own, with no booking attached.
/// </summary>
/// <remarks>
/// The payment carries only a family fee. That matters twice over: SettlePaymentAsync sets the
/// family flag because FamilyFeeOre is positive, and leaves the expiry date alone because
/// MembershipFeeOre is zero. See Pricing.FamilyUpgradeQuote for why moving the date would make the
/// supplement a cheaper renewal than the annual fee itself.
/// </remarks>
public sealed class FamilyUpgradeSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IPublishedContentQuery contentQuery,
    IBookingRepository repository,
    MemberProfileService profiles,
    MembershipSettingsService settings,
    IPaymentProvider paymentProvider,
    ILogger<FamilyUpgradeSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Upgrade()
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("A family upgrade was attempted with no signed-in member.");
            return Forbid();
        }

        MemberState member = await profiles.GetStateAsync(user.Key);
        if (member.IsFamilyAccount)
        {
            // Not an error worth alarming anyone about: two tabs, or a back button.
            TempData["ChildMessage"] = "Kontot är redan ett familjekonto.";
            return RedirectToCurrentUmbracoPage();
        }

        MembershipSettings config = settings.Get();
        BookingQuote quote = Pricing.FamilyUpgradeQuote(config.Prices);

        var payment = new PaymentRecord
        {
            Reference = Guid.NewGuid(),
            MemberKey = user.Key,

            // No booking: this buys a capability, not a place on a class.
            BookingId = null,
            AmountOre = quote.TotalOre,
            MembershipFeeOre = 0,
            FamilyFeeOre = quote.FamilyDueOre,
            ClassFeeOre = 0,
            Status = PaymentStatus.Pending,
            Provider = paymentProvider.Name,
            CreatedUtc = DateTime.UtcNow,
        };

        await repository.CreatePaymentAsync(payment);

        var paymentUrl = PaymentPageUrl(payment.Reference);
        if (paymentUrl is null)
        {
            logger.LogError("The payment page is missing; cannot send the member to pay.");
            TempData["ChildError"] =
                "Betalsidan saknas. Kontakta oss på info@ndstk.se så hjälper vi dig.";
            return RedirectToCurrentUmbracoPage();
        }

        logger.LogInformation(
            "Family upgrade payment {Reference} created for {MemberKey}.", payment.Reference, user.Key);

        return Redirect(paymentUrl);
    }

    private string? PaymentPageUrl(Guid reference)
    {
        IPublishedContent? page = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("swishPayment"))
            .FirstOrDefault();

        return page is null
            ? null
            : $"{page.Url(publishedUrlProvider)}?ref={Uri.EscapeDataString(reference.ToString())}";
    }
}
