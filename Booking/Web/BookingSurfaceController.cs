using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Filters;
using Umbraco.Cms.Web.Website.Controllers;

namespace NDSTK.Booking.Web;

/// <summary>Takes the booking request from the portal.</summary>
public sealed class BookingSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IPublishedContentQuery contentQuery,
    BookingService bookings,
    MembershipSettingsService settings,
    ILogger<BookingSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Book(Guid participantKey, Guid classKey, bool useCredit = false)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            // The portal is behind public access, so this should be unreachable. Fail closed.
            logger.LogWarning("A booking was attempted with no signed-in member.");
            return Forbid();
        }

        BookingAttempt attempt = await bookings.BookAsync(user.Key, participantKey, classKey, useCredit);

        if (attempt.Succeeded is false)
        {
            TempData["BookingError"] = MessageFor(attempt.Failure);
            return RedirectToCurrentUmbracoPage();
        }

        if (attempt.NeedsPayment)
        {
            var paymentUrl = PaymentPageUrl.For(contentQuery, PublishedUrlProvider, attempt.PaymentReference!.Value);
            if (paymentUrl is null)
            {
                // Rather than leave the member holding an unpayable reservation, release it.
                logger.LogError("The payment page is missing; cannot send the member to pay.");
                TempData["BookingError"] =
                    "Betalsidan saknas. Kontakta oss på info@ndstk.se så hjälper vi dig.";
                return RedirectToCurrentUmbracoPage();
            }

            return Redirect(paymentUrl);
        }

        TempData["BookingMessage"] = "Klart! Din träning är bokad med en tillgodoträning.";
        return RedirectToCurrentUmbracoPage();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Cancel(int bookingId)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("A cancellation was attempted with no signed-in member.");
            return Forbid();
        }

        switch (await bookings.CancelAsync(user.Key, bookingId))
        {
            case CancelOutcome.Cancelled:
                TempData["BookingMessage"] =
                    "Bokningen är avbokad. Avgiften betalas inte tillbaka, men du har fått en "
                    + "tillgodoträning att boka en annan gång med.";
                break;

            case CancelOutcome.TooLate:
                // Its own message, and it names the rule. This one is only ever reached for the
                // member's own confirmed booking, so it reveals nothing they did not already know,
                // and "kan inte avbokas" would read as a fault rather than a deadline.
                TempData["BookingError"] =
                    $"Träningen börjar för snart. Avbokning stänger "
                    + $"{settings.Get().CancellationDeadlineHours} timmar före start.";
                break;

            default:
                // Deliberately one message for every other reason: not yours, not confirmed.
                // Distinguishing them would tell a member whether a booking id they guessed exists.
                TempData["BookingError"] = "Den bokningen kan inte avbokas.";
                break;
        }

        return RedirectToCurrentUmbracoPage();
    }

    /// <summary>
    /// Swedish, member-facing, and deliberately specific: "det gick inte" tells someone nothing
    /// about whether to try a different class or wait.
    /// </summary>
    private static string MessageFor(BookingFailure failure) => failure switch
    {
        BookingFailure.ClassNotFound => "Träningen finns inte längre.",
        BookingFailure.ClassHasStarted => "Träningen har redan börjat.",
        BookingFailure.ClassIsFull => "Någon hann före – träningen är fullbokad.",
        BookingFailure.AlreadyBooked => "Barnet är redan bokat på den träningen.",
        BookingFailure.NoCreditAvailable => "Du har ingen tillgodoträning att använda.",
        BookingFailure.ParticipantNotFound => "Välj vilket barn bokningen gäller.",
        BookingFailure.ParticipantIncomplete => "Fyll i barnets födelsedatum under Mina barn innan du bokar.",
        _ => "Något gick fel. Försök igen om en liten stund.",
    };
}
