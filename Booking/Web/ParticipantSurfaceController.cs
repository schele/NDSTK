using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Services;
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

/// <summary>
/// "Mina barn": the member manages the children on their own account.
/// </summary>
/// <remarks>
/// Every action verifies ownership through the repository's conditional UPDATE rather than reading
/// first and trusting the result, so a forged key in a POST changes nothing rather than racing a
/// check that passed.
///
/// The portal is behind Umbraco's public access, so an anonymous visitor never reaches these. The
/// null check on the current member is a belt-and-braces guard, not the gate.
/// </remarks>
public sealed class ParticipantSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IParticipantRepository participants,
    IBookingRepository bookings,
    MemberProfileService profiles,
    ILogger<ParticipantSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Add(ParticipantFormModel form)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("A child was added with no signed-in member.");
            return Forbid();
        }

        // The supplement is what buys more than one child. This is the rule; the view only hides
        // the button, and a hidden button is not a rule.
        MemberState member = await profiles.GetStateAsync(user.Key);
        IReadOnlyList<ParticipantRecord> existing = await participants.GetForMemberAsync(user.Key);

        if (member.IsFamilyAccount is false && existing.Count >= 1)
        {
            TempData["ChildError"] = "Uppgradera till familjekonto för att lägga till fler barn.";
            return RedirectToCurrentUmbracoPage();
        }

        if (Validate(form, out DateOnly birthDate) is { } error)
        {
            TempData["ChildError"] = error;
            return RedirectToCurrentUmbracoPage();
        }

        var name = form.FirstName.Trim();

        // A child who was removed and is being added back is the same person, not a new one. The
        // welcome price lives on the participant, so creating a second row would hand them a trial
        // class they had already used - and split their bookings across two rows nobody can pair up.
        Guid? restored = await participants.TryRestoreAsync(
            user.Key, name, form.LastName.Trim(), birthDate);

        if (restored is not null)
        {
            TempData["ChildMessage"] =
                $"{name} är tillagd igen. Tidigare bokningar finns kvar, och ett välkomstpris som "
                + "redan använts räknas fortfarande som använt.";

            return RedirectToCurrentUmbracoPage();
        }

        await participants.CreateAsync(
            user.Key, name, form.LastName.Trim(), birthDate, DateTime.UtcNow);

        TempData["ChildMessage"] = $"{name} är tillagd.";
        return RedirectToCurrentUmbracoPage();
    }

    /// <summary>
    /// Fills in a child the backfill could only guess at. Not a general edit.
    /// </summary>
    /// <remarks>
    /// A saved child's name and birth date are fixed: they identify a person on a class roster, and
    /// a coach cannot trust a list a parent can rewrite afterwards. The repository enforces that in
    /// the UPDATE - it only touches a row whose birth date is still null - so this stays true even
    /// though the form is not rendered for a completed child.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Complete(ParticipantFormModel form)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("A child was completed with no signed-in member.");
            return Forbid();
        }

        if (Validate(form, out DateOnly birthDate) is { } error)
        {
            TempData["ChildError"] = error;
            return RedirectToCurrentUmbracoPage();
        }

        var completed = await participants.TryCompleteAsync(
            form.Key, user.Key, form.FirstName.Trim(), form.LastName.Trim(), birthDate);

        TempData[completed ? "ChildMessage" : "ChildError"] = completed
            ? $"{form.FirstName.Trim()} är ifylld och kan nu bokas."
            : "Uppgifterna kunde inte sparas. Ett barn som redan är ifyllt går inte att ändra.";

        return RedirectToCurrentUmbracoPage();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Remove(Guid key)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            logger.LogWarning("A child was removed with no signed-in member.");
            return Forbid();
        }

        // An account with no children can never book anything, so the last one stays.
        IReadOnlyList<ParticipantRecord> existing = await participants.GetForMemberAsync(user.Key);
        if (existing.Count <= 1)
        {
            TempData["ChildError"] = "Kontot måste ha minst ett barn.";
            return RedirectToCurrentUmbracoPage();
        }

        var removed = await participants.TryRemoveAsync(key, user.Key, DateTime.UtcNow);
        if (removed is false)
        {
            TempData["ChildError"] = "Barnet hittades inte.";
            return RedirectToCurrentUmbracoPage();
        }

        // Their future bookings go with them. Left standing, the seat stays reserved against the
        // class's capacity and the child keeps appearing on the coach's roster, while the parent
        // believes they are gone - wrong in both directions.
        //
        // This does exactly what the member pressing "Avboka" on each booking would do, credit and
        // all. Past bookings are untouched: cancelling those would rewrite attendance that already
        // happened and mint a credit for a class the child went to.
        (var cancelled, var credited) = await bookings.CancelFutureBookingsForParticipantAsync(
            key, user.Key, DateTime.UtcNow);

        // The name comes from the list fetched above, which is this account's own children, so it
        // cannot surface somebody else's - and it is read before the removal because afterwards the
        // row is soft-deleted and GetForMemberAsync no longer returns it.
        var removedName = existing.FirstOrDefault(child => child.Key == key)?.FirstName;

        var message = removedName is { Length: > 0 } name
            ? $"{name} togs bort. Tidigare bokningar finns kvar."
            : "Barnet togs bort. Tidigare bokningar finns kvar.";

        if (cancelled > 0)
        {
            message += $" {cancelled} kommande {(cancelled == 1 ? "bokning" : "bokningar")} avbokades";
            message += credited > 0
                ? $" och du fick {credited} {(credited == 1 ? "tillgodoträning" : "tillgodoträningar")}."
                : ".";
        }

        // Down to one child, so the account is no longer a family. Left alone, the supplement would
        // keep being charged at every renewal for ever, with nothing in the portal to stop it.
        //
        // Nothing is refunded and nothing is lost: the supplement already paid covers the rest of
        // this membership year, and re-activating inside it is free. See
        // FamilyUpgradeSurfaceController.
        IReadOnlyList<ParticipantRecord> remaining = await participants.GetForMemberAsync(user.Key);
        MemberState member = await profiles.GetStateAsync(user.Key);

        if (remaining.Count <= 1 && member.IsFamilyAccount)
        {
            await profiles.ClearFamilyAccountAsync(user.Key);
            message += " Kontot är nu ett solokonto, så årsavgiften blir lägre vid nästa förnyelse.";
        }

        TempData["ChildMessage"] = message;
        return RedirectToCurrentUmbracoPage();
    }

    /// <summary>The message to show, or null when the form is fine.</summary>
    private static string? Validate(ParticipantFormModel form, out DateOnly birthDate)
    {
        birthDate = default;

        if (string.IsNullOrWhiteSpace(form.FirstName) || string.IsNullOrWhiteSpace(form.LastName))
        {
            return "Ange både förnamn och efternamn.";
        }

        if (SwedishDate.TryParseCompact(form.BirthDate, out birthDate) is false)
        {
            return "Skriv födelsedatumet som ÅÅÅÅMMDD, till exempel 20170413.";
        }

        return birthDate > DateOnly.FromDateTime(SwedishTime.ToSwedish(DateTime.UtcNow))
            ? "Födelsedatumet ligger i framtiden."
            : null;
    }
}
