using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.ContentModel;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Umbraco.Cms.Web.Common.Controllers;

namespace NDSTK.Booking.Web;

/// <summary>
/// Handles the link in the verification mail.
/// </summary>
/// <remarks>
/// Route hijacking: naming this after the document type means Umbraco routes the memberVerify page
/// through it automatically, so the confirmation happens on the GET that the emailed link performs
/// - no extra endpoint, and the page's copy stays editable content.
/// </remarks>
public sealed class MemberVerifyController(
    ILogger<MemberVerifyController> logger,
    ICompositeViewEngine compositeViewEngine,
    IUmbracoContextAccessor umbracoContextAccessor,
    IMemberManager memberManager,
    IMemberService memberService)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    /// <summary>
    /// Named after the <c>MemberVerify</c> template, not <c>Index</c>. Umbraco's route hijacking
    /// looks for an action matching the template alias and only falls back to <c>Index</c>, and
    /// <see cref="RenderController.Index"/> is a sync virtual method - an async <c>Index</c>
    /// overload beside it registers two endpoints with the same name and every request fails with
    /// AmbiguousMatchException. Matching the template alias gives an async action with no clash.
    /// </summary>
    [EnableRateLimiting(BookingRateLimits.MemberForms)]
    public async Task<IActionResult> MemberVerify(
        [FromQuery] string? member, [FromQuery] string? token)
    {
        ViewData["VerifyOutcome"] = await ConfirmAsync(member, token);
        return CurrentTemplate(CurrentPage);
    }

    private async Task<string> ConfirmAsync(string? member, string? token)
    {
        if (Guid.TryParse(member, out Guid memberKey) is false || string.IsNullOrWhiteSpace(token))
        {
            return VerifyOutcome.Invalid;
        }

        MemberIdentityUser? user = await memberManager.FindByIdAsync(memberKey.ToString());
        if (user is null)
        {
            // Same answer as a bad token: a "no such member" response would let someone test which
            // member keys exist.
            logger.LogInformation("Verification attempted for an unknown member.");
            return VerifyOutcome.Invalid;
        }

        var alreadyActive = user.EmailConfirmed && user.IsApproved;

        // The token is validated BEFORE the account's state is allowed to influence the answer.
        // Checking "already active" first would answer "redan aktiverat" to any token at all, which
        // turns this page into an oracle: anyone holding a member's key could learn whether that
        // account is active. Harmless while member keys never reach the front end - but the booking
        // tables are keyed by MemberKey, so that invariant has a lot of future code to survive, and
        // relying on it is not worth the saving.
        //
        // Nothing is lost by ordering it this way. Identity's confirmation token stays valid until
        // it expires - ConfirmEmailAsync does not rotate the security stamp - so a second click on
        // the same day, or a mail client prefetching the link, still presents a valid token and
        // still gets the friendly "already activated" below. Only a re-click after the token has
        // expired falls through to the generic error, which is rare and costs the member nothing:
        // their account already works.
        IdentityResult confirmed = await memberManager.ConfirmEmailAsync(user, token);
        if (confirmed.Succeeded is false)
        {
            logger.LogInformation(
                "Verification token rejected for member {Key}: {Errors}",
                memberKey, string.Join("; ", confirmed.Errors.Select(error => error.Code)));
            return VerifyOutcome.Invalid;
        }

        if (alreadyActive)
        {
            return VerifyOutcome.AlreadyDone;
        }

        // Approval is the gate that actually lets the member sign in. Umbraco's own sign-in refuses
        // an unapproved member, so this line is what turns a registered account into a usable one.
        user.IsApproved = true;
        IdentityResult approved = await memberManager.UpdateAsync(user);
        if (approved.Succeeded is false)
        {
            logger.LogError(
                "Confirmed the email for member {Key} but could not approve them: {Errors}",
                memberKey, string.Join("; ", approved.Errors.Select(error => error.Code)));
            return VerifyOutcome.Invalid;
        }

        // Group membership is what Umbraco's public access actually checks, so without this the
        // member could sign in and then be bounced straight back off their own portal.
        //
        // IMemberManager exposes no role methods - it is a deliberately narrow interface - so this
        // goes through IMemberService, which gets AssignRole from IMembershipRoleService<IMember>.
        // AssignRole is idempotent, so no "is already in the group" check is needed.
        AssignToMemberGroup(user.UserName!);

        logger.LogInformation("Member {Key} verified their email address.", memberKey);
        return VerifyOutcome.Confirmed;
    }

    /// <summary>
    /// Adds the member to the Medlemmar group. Failures are logged, not thrown: the account is
    /// already verified and usable, and an administrator can fix group membership in the
    /// backoffice - failing the whole verification here would be worse for the member.
    /// </summary>
    private void AssignToMemberGroup(string username)
    {
        try
        {
            memberService.AssignRole(username, NdstkMemberAccessInstaller.MemberGroupName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "Could not add a verified member to the {Group} group.",
                NdstkMemberAccessInstaller.MemberGroupName);
        }
    }
}

/// <summary>The three things the verify page can say, kept as constants so the view cannot typo one.</summary>
public static class VerifyOutcome
{
    public const string Confirmed = "confirmed";
    public const string AlreadyDone = "already";
    public const string Invalid = "invalid";
}
