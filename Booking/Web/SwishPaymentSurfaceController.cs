using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
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
/// The two simulate buttons on the mocked Swish page.
/// </summary>
/// <remarks>
/// These stand in for what a real Swish integration would receive as a server-to-server callback.
/// Both are POSTs with antiforgery, and both verify that the payment belongs to the signed-in
/// member - a GET, or a missing ownership check, would let anyone settle anyone's payment by URL.
/// </remarks>
public sealed class SwishPaymentSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IPublishedContentQuery contentQuery,
    IBookingRepository repository,
    BookingService bookings,
    ILogger<SwishPaymentSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulatePaid(Guid reference)
    {
        PaymentRecord? payment = await OwnedPendingPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        await bookings.SettlePaymentAsync(payment);

        TempData["BookingMessage"] = "Betalningen är genomförd och din träning är bokad.";
        return Redirect(PortalUrl());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulateCancelled(Guid reference)
    {
        PaymentRecord? payment = await OwnedPendingPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        await bookings.AbandonPaymentAsync(payment, PaymentStatus.Cancelled);

        TempData["BookingError"] = "Betalningen avbröts, så platsen är inte bokad.";
        return Redirect(PortalUrl());
    }

    /// <summary>
    /// Loads the payment only if it belongs to the signed-in member and is still pending. Settling
    /// an already-settled payment would extend a membership twice, so the status check is as
    /// important as the ownership one.
    /// </summary>
    private async Task<PaymentRecord?> OwnedPendingPaymentAsync(Guid reference)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            return null;
        }

        PaymentRecord? payment = await repository.GetPaymentByReferenceAsync(reference);

        if (payment is null || payment.MemberKey != user.Key)
        {
            logger.LogWarning("A payment action was attempted by someone who does not own it.");
            return null;
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            logger.LogInformation(
                "Payment {Reference} is already {Status}; ignoring a repeated action.",
                reference, payment.Status);
            return null;
        }

        return payment;
    }

    private string PortalUrl()
    {
        IPublishedContent? portal = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("memberPortal"))
            .FirstOrDefault();

        return portal?.Url(publishedUrlProvider) ?? "/";
    }
}
