using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Filters;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>Signs members in and out.</summary>
public sealed class LoginSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IMemberSignInManager signInManager,
    MembershipSettingsService settings,
    ILogger<LoginSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    /// <summary>
    /// Deliberately says nothing about which half was wrong, so the form cannot be used to find out
    /// who is a member of the club.
    /// </summary>
    private const string BadCredentialsMessage = "Fel e-postadress eller lösenord.";

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.Auth)]
    public async Task<IActionResult> Login(LoginFormModel form)
    {
        if (ModelState.IsValid is false)
        {
            return CurrentUmbracoPage();
        }

        var email = form.Email.Trim();

        // lockoutOnFailure honours Umbraco:CMS:Security:MemberDefaultLockoutTimeInMinutes, so
        // repeated guesses against one account stop being useful.
        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.PasswordSignInAsync(
            email, form.Password, form.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("A member signed in.");
            return Redirect(AfterLoginUrl());
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Kontot är tillfälligt låst efter för många försök. Prova igen om en stund.");
            return CurrentUmbracoPage();
        }

        // NotAllowed means the account exists but may not sign in - for us, that it has not been
        // verified yet. Identity returns this from its pre-sign-in check, BEFORE the password is
        // verified, so saying "activate your account" here outright would tell anyone typing any
        // password that the address is registered. The password is therefore checked explicitly
        // first: only someone who already knows it learns anything.
        if (result.IsNotAllowed && await PasswordIsCorrectAsync(email, form.Password))
        {
            ModelState.AddModelError(string.Empty,
                "Kontot är inte aktiverat än. Klicka på länken i mailet vi skickade när du registrerade dig.");
            return CurrentUmbracoPage();
        }

        ModelState.AddModelError(string.Empty, BadCredentialsMessage);
        return CurrentUmbracoPage();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        logger.LogInformation("A member signed out.");

        IPublishedContent? home = CurrentPage?.Root();
        return Redirect(home?.Url(publishedUrlProvider) ?? "/");
    }

    private async Task<bool> PasswordIsCorrectAsync(string email, string password)
    {
        MemberIdentityUser? user = await memberManager.FindByEmailAsync(email);
        return user is not null && await memberManager.CheckPasswordAsync(user, password);
    }

    /// <summary>
    /// The member portal if an editor has picked one on the Settings node, otherwise the start
    /// page. Falling back rather than failing means login keeps working before the portal exists.
    /// </summary>
    private string AfterLoginUrl()
    {
        IPublishedContent? portal = settings.GetMemberPortalPage();
        if (portal is not null)
        {
            return portal.Url(publishedUrlProvider);
        }

        return CurrentPage?.Root()?.Url(publishedUrlProvider) ?? "/";
    }
}
