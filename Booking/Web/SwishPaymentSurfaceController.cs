using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Payments.Swish;
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
/// The actions behind the payment page: start the Swish request, report where it stands, draw its
/// QR code, cancel it. Plus the two simulate buttons, which exist only while the mock is the
/// provider.
/// </summary>
/// <remarks>
/// Every action loads the payment through <see cref="OwnedPaymentAsync"/>, which verifies it
/// belongs to the signed-in member and answers "not found" otherwise - a reference must not be
/// probeable for existence.
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
    IPaymentProvider paymentProvider,
    SwishCallbackUrl callbackUrl,
    SwishQrService qr,
    ILogger<SwishPaymentSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    private const string ProviderUnavailableMessage =
        "Swish går inte att nå just nu. Försök igen om en stund.";

    /// <summary>The seconds a poll waits before asking Swish again for the same payment.</summary>
    private static readonly TimeSpan PollSpacing = TimeSpan.FromSeconds(5);

    private bool IsMock => paymentProvider.Name == SwishMockPaymentProvider.ProviderName;

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Start(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        StartPaymentResult result = await bookings.StartPaymentAsync(payment, callbackUrl.Build());

        if (result == StartPaymentResult.ProviderUnavailable)
        {
            TempData["PaymentError"] = ProviderUnavailableMessage;
        }

        return Redirect(PaymentPageUrl.For(contentQuery, PublishedUrlProvider, reference) ?? PortalUrl());
    }

    /// <summary>
    /// Where the payment stands, as JSON for the page's poll. Asks Swish when it is pending, has a
    /// request, and was not asked in the last few seconds.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(BookingRateLimits.PaymentStatus)]
    public async Task<IActionResult> Status(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        DateTime nowUtc = DateTime.UtcNow;

        var due = payment.Status == PaymentStatus.Pending
            && payment.ProviderReference is not null
            && (payment.LastCheckedUtc is null || payment.LastCheckedUtc <= nowUtc - PollSpacing);

        if (due)
        {
            try
            {
                payment = await bookings.ReconcileAsync(payment, nowUtc);
            }
            catch (PaymentProviderException exception)
            {
                // The poll will try again. The member sees "väntar", which is the truth.
                logger.LogWarning(exception, "Reconciling payment {Reference} from the page failed.", reference);
            }
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new { status = payment.Status, terminal = payment.Status != PaymentStatus.Pending });
    }

    [HttpGet]
    [EnableRateLimiting(BookingRateLimits.PaymentStatus)]
    public async Task<IActionResult> Qr(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment?.ProviderToken is null || IsMock)
        {
            return NotFound();
        }

        var svg = await qr.GetSvgAsync(payment.Reference, payment.ProviderToken);
        if (svg is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=600";
        return File(svg, "image/svg+xml");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Cancel(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        switch (await bookings.CancelPaymentAsync(payment))
        {
            case CancelPaymentResult.Cancelled:
                TempData["BookingError"] = "Betalningen avbröts, så platsen är inte bokad.";
                return Redirect(PortalUrl());

            case CancelPaymentResult.ProviderUnavailable:
                TempData["PaymentError"] = ProviderUnavailableMessage;
                break;
        }

        // AlreadyFinal: the page shows what Swish decided.
        return Redirect(PaymentPageUrl.For(contentQuery, PublishedUrlProvider, reference) ?? PortalUrl());
    }

    // ------------------------------------------------------------ the mock's buttons

    /// <summary>Stands in for a PAID callback. Only while the mock is the provider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulatePaid(Guid reference)
    {
        if (IsMock is false)
        {
            return NotFound();
        }

        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return NotFound();
        }

        SettlementResult result = await bookings.SettlePaymentAsync(payment);

        TempData["BookingMessage"] = result switch
        {
            SettlementResult.Credited =>
                "Betalningen är genomförd. Platsen hann ta slut, så du har fått en tillgodoträning i stället.",
            SettlementResult.NoBooking => "Betalningen är genomförd. Kontot är nu ett familjekonto.",
            SettlementResult.AlreadySettled => "Betalningen var redan genomförd.",
            _ => "Betalningen är genomförd och din träning är bokad.",
        };
        return Redirect(PortalUrl());
    }

    /// <summary>Stands in for a DECLINED callback. Only while the mock is the provider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulateCancelled(Guid reference)
    {
        if (IsMock is false)
        {
            return NotFound();
        }

        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return NotFound();
        }

        await bookings.AbandonPaymentAsync(payment, PaymentStatus.Cancelled);

        TempData["BookingError"] = "Betalningen avbröts, så platsen är inte bokad.";
        return Redirect(PortalUrl());
    }

    /// <summary>
    /// The payment, only if it belongs to the signed-in member. "Not found" for anything else, so
    /// a reference cannot be probed for existence.
    /// </summary>
    private async Task<PaymentRecord?> OwnedPaymentAsync(Guid reference)
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

        return payment;
    }

    private string PortalUrl()
    {
        IPublishedContent? portal = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("memberPortal"))
            .FirstOrDefault();

        return portal?.Url(PublishedUrlProvider) ?? "/";
    }
}
