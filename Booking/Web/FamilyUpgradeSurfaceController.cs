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
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(DateTime.UtcNow));

        // Nothing to sell yet.
        //
        // The supplement buys family status for the remainder of the CURRENT membership year, and a
        // member who has never paid - or who has lapsed - has no current year for it to cover. Their
        // next booking will renew the membership, and Pricing.Quote charges the supplement alongside
        // the annual fee whenever this flag is set. Taking 100 kr here as well would charge them
        // twice for one thing, minutes apart.
        //
        // So the flag is simply set, free, and the money is collected once, on the booking that
        // creates the membership year it belongs to.
        if (Pricing.IsMembershipValid(member, today) is false)
        {
            await profiles.SetFamilyAccountAsync(user.Key);

            TempData["ChildMessage"] =
                $"Familjekonto aktiverat. Tillägget på {config.Prices.FamilyFeeOre / 100} kr läggs "
                + "till tillsammans med årsavgiften på din nästa bokning.";

            logger.LogInformation(
                "Member {MemberKey} became a family account with no membership to charge against; "
                + "the supplement rides along with their next booking.", user.Key);

            return RedirectToCurrentUmbracoPage();
        }

        // Already paid for, this year.
        //
        // A member who dropped back to one child was downgraded on their behalf. Charging them
        // again to undo that would bill the same supplement twice inside one membership year, which
        // is the mistake that made the standalone upgrade wrong in the first place.
        //
        // The year runs backwards from the expiry, because that is how the expiry was set: the
        // payment that created it stamped today + 365.
        DateTime yearStartUtc = member.MembershipPaidUntil!.Value
            .AddDays(-Pricing.MembershipDays)
            .ToDateTime(TimeOnly.MinValue);

        if (await repository.HasPaidFamilyFeeSinceAsync(user.Key, yearStartUtc))
        {
            await profiles.SetFamilyAccountAsync(user.Key);

            TempData["ChildMessage"] =
                "Familjekontot är aktiverat igen. Du har redan betalat tillägget för det här året.";

            logger.LogInformation(
                "Member {MemberKey} re-activated their family account inside a year they had "
                + "already paid the supplement for; nothing was charged.", user.Key);

            return RedirectToCurrentUmbracoPage();
        }

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
