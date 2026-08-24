using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Services;
using NDSTK.ContentModel;
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
/// Handles the "Bli medlem" registration form.
/// </summary>
/// <remarks>
/// The account is created unapproved and stays that way until the emailed link is followed.
/// Umbraco's own member sign-in refuses an unapproved member, so an unverified account cannot log
/// in even if the check in the login controller were somehow bypassed.
/// </remarks>
public sealed class RegisterSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IPublishedContentQuery contentQuery,
    BookingMailService mailService,
    ILogger<RegisterSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    /// <summary>
    /// Shown whether or not the address was already taken. Revealing "that email is taken" would
    /// turn the form into a tool for discovering who is a member of the club.
    /// </summary>
    private const string CheckYourInboxMessage =
        "Tack! Om adressen kan användas har vi skickat ett bekräftelsemail. Kolla din inkorg – och skräpposten.";

    /// <summary>A form filled in faster than this was not typed by a person.</summary>
    private static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(2);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberForms)]
    public async Task<IActionResult> Register(RegisterFormModel form)
    {
        if (IsProbablyABot(form))
        {
            // Answered exactly as a success would be, so a bot learns nothing from the difference.
            logger.LogInformation("Discarded a registration that looks automated.");
            TempData["RegisterMessage"] = CheckYourInboxMessage;
            return RedirectToCurrentUmbracoPage();
        }

        if (ModelState.IsValid is false)
        {
            return CurrentUmbracoPage();
        }

        var email = form.Email.Trim();

        var user = MemberIdentityUser.CreateNew(
            username: email,
            email: email,
            memberTypeAlias: NdstkKeys.MemberTypes.MemberAlias,
            isApproved: false);

        // One call does both the password policy (Umbraco:CMS:Security:MemberPassword) and the
        // uniqueness check. Doing them in one step is what keeps the responses leak-free - see
        // below.
        IdentityResult created = await memberManager.CreateAsync(user, form.Password);

        if (created.Succeeded)
        {
            await SendVerificationMailAsync(user);
            TempData["RegisterMessage"] = CheckYourInboxMessage;
            return RedirectToCurrentUmbracoPage();
        }

        // Ordering here is a security property, not a style choice.
        //
        // Password complaints are reported, because they are true of the password whatever address
        // it was paired with, so reporting them reveals nothing about who is a member. A duplicate
        // address, on the other hand, gets the same response as success.
        //
        // Password errors are therefore checked FIRST. If a duplicate address short-circuited
        // ahead of them, an attacker could submit a deliberately weak password and read the
        // difference: "check your inbox" would mean the address exists, a password error would mean
        // it is free. Handling the password first makes a strong-password attempt look identical
        // either way, and a weak-password attempt fail identically either way.
        var passwordErrors = created.Errors
            .Where(error => error.Code.Contains("Password", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (passwordErrors.Count > 0)
        {
            foreach (IdentityError error in passwordErrors)
            {
                ModelState.AddModelError(nameof(form.Password), error.Description);
            }

            return CurrentUmbracoPage();
        }

        var isDuplicate = created.Errors.Any(error =>
            error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            // Indistinguishable from success, and deliberately sends no mail - otherwise the form
            // would be a way to spam an existing member.
            logger.LogInformation("Registration attempted for an address that already exists.");
            TempData["RegisterMessage"] = CheckYourInboxMessage;
            return RedirectToCurrentUmbracoPage();
        }

        logger.LogWarning(
            "Could not create a member: {Errors}",
            string.Join("; ", created.Errors.Select(error => error.Code)));

        ModelState.AddModelError(string.Empty, "Något gick fel. Försök igen om en liten stund.");
        return CurrentUmbracoPage();
    }

    private async Task SendVerificationMailAsync(MemberIdentityUser user)
    {
        var token = await memberManager.GenerateEmailConfirmationTokenAsync(user);

        // The verify page is a sibling of this one. Resolved from content rather than hard-coded so
        // an editor can rename or move it.
        var verifyUrl = VerifyPageUrl(user.Key, token);
        if (verifyUrl is null)
        {
            logger.LogError(
                "Created member {Key} but the verification page could not be found, so no mail was sent.",
                user.Key);
            return;
        }

        await mailService.SendVerificationAsync(user.Email!, verifyUrl);
    }

    private string? VerifyPageUrl(Guid memberKey, string token)
    {
        var verifyPage = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("memberVerify"))
            .FirstOrDefault();

        if (verifyPage is null)
        {
            return null;
        }

        var baseUrl = verifyPage.Url(publishedUrlProvider, mode: UrlMode.Absolute);

        // The token is base64 with +, / and = in it, so it must be escaped or the query string
        // arrives mangled and confirmation always fails.
        return $"{baseUrl}?member={Uri.EscapeDataString(memberKey.ToString())}"
               + $"&token={Uri.EscapeDataString(token)}";
    }

    private bool IsProbablyABot(RegisterFormModel form)
    {
        if (string.IsNullOrWhiteSpace(form.Website) is false)
        {
            return true;
        }

        if (form.RenderedAt <= 0)
        {
            return false;
        }

        DateTimeOffset rendered = DateTimeOffset.FromUnixTimeSeconds(form.RenderedAt);
        return DateTimeOffset.UtcNow - rendered < MinimumFillTime;
    }

}
