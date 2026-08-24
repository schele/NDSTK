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
    IBookingRepository bookings)
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
        IReadOnlyList<BookingSnapshot> mine = await bookings.GetBookingsForMemberAsync(memberKey);
        IReadOnlyList<CreditSnapshot> credits = await bookings.GetCreditsForMemberAsync(memberKey);

        ViewData["MemberPortal"] = new MemberPortalViewModel(
            UpcomingClasses: upcoming,
            MyBookings: BuildRows(mine, credits),
            UnspentCredits: Credits.CountUnspent(credits),
            Membership: new MembershipStatus(
                Pricing.IsMembershipValid(state, today), state.MembershipPaidUntil),
            Prices: config.Prices,
            FirstClassDiscountAvailable: state.FirstClassDiscountUsed is false,
            ReminderHoursBefore: config.ReminderHoursBefore);

        return CurrentTemplate(CurrentPage);
    }

    /// <summary>
    /// Pairs each booking with its class and with the credit that paid for it, if any.
    /// </summary>
    /// <remarks>
    /// Expired holds are dropped: they are bookkeeping for an abandoned payment, not something the
    /// member ever chose, and listing them would only confuse.
    ///
    /// The class lookup may come back null when an editor has deleted the class node. The row still
    /// renders, because the booking carries its own copy of the start time - a member who paid
    /// deserves to see the booking either way.
    /// </remarks>
    private List<MemberBookingRow> BuildRows(
        IReadOnlyList<BookingSnapshot> snapshots, IReadOnlyList<CreditSnapshot> credits)
    {
        HashSet<int> paidByCredit =
        [
            .. credits
                .Where(credit => credit.SpentOnBookingId is not null)
                .Select(credit => credit.SpentOnBookingId!.Value),
        ];

        return
        [
            .. snapshots
                .Where(snapshot => snapshot.Status is BookingStatus.Confirmed or BookingStatus.Cancelled)
                .OrderByDescending(snapshot => snapshot.ClassStartUtc)
                .Select(snapshot => new MemberBookingRow(
                    snapshot.Id,
                    classes.Find(snapshot.ClassKey),
                    snapshot.Status,
                    snapshot.ClassStartUtc,
                    UsedCredit: paidByCredit.Contains(snapshot.Id))),
        ];
    }
}
