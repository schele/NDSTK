using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;

namespace NDSTK.Booking.Web;

/// <summary>
/// Assembles the member portal.
/// </summary>
/// <remarks>
/// Named after the MemberPortal template, not Index - see MemberVerifyController for why an async
/// Index overload beside RenderController's sync one breaks every request.
///
/// Access is not enforced here. The portal node is protected with Umbraco's public access, so an
/// anonymous visitor is redirected by the pipeline before this controller runs. The null check on
/// the current member is a belt-and-braces guard, not the gate.
/// </remarks>
public sealed class MemberPortalController(
    ILogger<MemberPortalController> logger,
    ICompositeViewEngine compositeViewEngine,
    IUmbracoContextAccessor umbracoContextAccessor,
    IMemberManager memberManager,
    TrainingClassService classes,
    MemberProfileService profiles,
    MembershipSettingsService settings,
    IBookingRepository bookings,
    MemberBookingsProvider bookingsProvider)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    public async Task<IActionResult> MemberPortal()
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("The member portal was reached without a signed-in member.");
            return CurrentTemplate(CurrentPage);
        }

        Guid memberKey = user.Key;
        DateTime nowUtc = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));

        MembershipSettings config = settings.Get();
        MemberState state = await profiles.GetStateAsync(memberKey);

        IReadOnlyList<BookableClass> upcoming = await classes.GetUpcomingAsync(memberKey, nowUtc);
        IReadOnlyList<MemberBookingRow> mine = await bookingsProvider.GetCurrentAsync(memberKey);
        IReadOnlyList<CreditSnapshot> credits = await bookings.GetCreditsForMemberAsync(memberKey);

        ViewData["MemberPortal"] = new MemberPortalViewModel(
            // Shown in the membership box so a member can see which account they are signed in as -
            // it doubles as the login name, so it is the one identifier worth confirming.
            Email: user.Email,
            UpcomingClasses: upcoming,
            MyBookings: mine,
            UnspentCredits: Credits.CountUnspent(credits),
            Membership: new MembershipStatus(
                Pricing.IsMembershipValid(state, today), state.MembershipPaidUntil),
            Prices: config.Prices,
            FirstClassDiscountAvailable: state.FirstClassDiscountUsed is false,
            ReminderHoursBefore: config.ReminderHoursBefore);

        return CurrentTemplate(CurrentPage);
    }

}
