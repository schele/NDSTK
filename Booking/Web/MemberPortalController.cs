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
    IParticipantRepository participants,
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

        IReadOnlyList<ParticipantRecord> children = await participants.GetForMemberAsync(memberKey);
        IReadOnlyList<MemberChildRow> childRows =
        [
            .. children.Select(child => new MemberChildRow(
                child.Key,
                child.FirstName,
                child.LastName,
                child.BirthDate is { } birthDate ? DateOnly.FromDateTime(birthDate) : null,
                FirstClassAvailable: child.FirstClassUsedUtc is null)),
        ];

        IReadOnlyList<BookableClass> upcoming =
            await classes.GetUpcomingAsync([.. childRows.Select(child => child.Key)], nowUtc);
        IReadOnlyList<MemberBookingRow> mine = await bookingsProvider.GetCurrentAsync(memberKey);
        IReadOnlyList<CreditSnapshot> credits = await bookings.GetCreditsForMemberAsync(memberKey);

        ViewData["MemberPortal"] = new MemberPortalViewModel(
            // Shown in the membership box so a member can see which account they are signed in as -
            // it doubles as the login name, so it is the one identifier worth confirming.
            Email: user.Email,
            UpcomingClasses: upcoming,
            MyBookings: mine,
            Children: childRows,
            UnspentCredits: Credits.CountUnspent(credits),
            Membership: new MembershipStatus(
                Pricing.IsMembershipValid(state, today),
                state.MembershipPaidUntil,
                state.IsFamilyAccount,
                await SupplementPaidThisYearAsync(memberKey, state)),
            Prices: config.Prices,
            ReminderHoursBefore: config.ReminderHoursBefore,
            CancellationDeadlineHours: config.CancellationDeadlineHours);

        return CurrentTemplate(CurrentPage);
    }

    /// <summary>
    /// Whether the family supplement has already been paid for the current membership year.
    /// </summary>
    /// <remarks>
    /// Only ever true for an account that was a family earlier in the year and has since dropped
    /// back to one child. Asked so the upgrade button can offer a free re-activation instead of
    /// quoting a price the controller would decline to charge.
    ///
    /// Skipped entirely when the membership is not valid: there is no current year to have paid
    /// for, and that case is already free.
    /// </remarks>
    private async Task<bool> SupplementPaidThisYearAsync(Guid memberKey, MemberState state)
    {
        if (state.MembershipPaidUntil is not { } paidUntil
            || Pricing.IsMembershipValid(state, DateOnly.FromDateTime(SwedishTime.ToSwedish(DateTime.UtcNow))) is false)
        {
            return false;
        }

        DateTime yearStartUtc = paidUntil.AddDays(-Pricing.MembershipDays).ToDateTime(TimeOnly.MinValue);
        return await bookings.HasPaidFamilyFeeSinceAsync(memberKey, yearStartUtc);
    }
}
