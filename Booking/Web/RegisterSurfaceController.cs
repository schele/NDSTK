using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Security;
using NDSTK.Booking.Services;
using NDSTK.ContentModel;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models;
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
    IParticipantRepository participants,
    IMemberService memberService,
    IdentityErrorMessages messages,
    ILogger<RegisterSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    /// <summary>
    /// Shown whether or not the address was already taken. Revealing "that email is taken" would
    /// turn the form into a tool for discovering who is a member of the club.
    /// </summary>
    /// <remarks>
    /// Still one string used by every branch, so the enumeration guarantee holds: the response is
    /// byte-identical whether the address was free, unverified or already active.
    /// </remarks>
    private static readonly string CheckYourInboxMessage =
        "Tack! Om adressen kan användas har vi skickat ett bekräftelsemail. Kolla din inkorg – och "
        + $"skräpposten. Länken i mailet gäller i {(int)MemberVerificationTokenOptions.Lifespan.TotalMinutes} minuter.";

    /// <summary>A form filled in faster than this was not typed by a person.</summary>
    private static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(2);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.Auth)]
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
            return RedisplayForm(form);
        }

        // Checked after the bot guards and before CreateAsync, so the password-errors-before-
        // duplicate-address ordering below - which is what keeps the response leak-free - is
        // untouched. A bad birth date is true of the value whatever address it was paired with,
        // so reporting it reveals nothing about who is a member.
        if (SwedishDate.TryParseCompact(form.ChildBirthDate, out DateOnly childBirthDate) is false)
        {
            ModelState.AddModelError(
                nameof(form.ChildBirthDate),
                "Skriv födelsedatumet som ÅÅÅÅMMDD, till exempel 20170413.");
            return RedisplayForm(form);
        }

        if (childBirthDate > DateOnly.FromDateTime(SwedishTime.ToSwedish(DateTime.UtcNow)))
        {
            ModelState.AddModelError(nameof(form.ChildBirthDate), "Födelsedatumet ligger i framtiden.");
            return RedisplayForm(form);
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
            await SaveGuardianDetailsAsync(user.Key, form);

            // Every account has at least one participant from the moment it exists, so nothing
            // downstream has to handle a member with nobody to book for.
            await participants.CreateAsync(
                user.Key,
                form.ChildFirstName.Trim(),
                form.ChildLastName.Trim(),
                childBirthDate,
                DateTime.UtcNow);

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
            // Identity's own Description is English - Umbraco localises some identity errors but not
            // the password ones - so it is translated rather than shown raw.
            foreach (IdentityError error in passwordErrors)
            {
                ModelState.AddModelError(nameof(form.Password), messages.Describe(error));
            }

            return RedisplayForm(form);
        }

        var isDuplicate = created.Errors.Any(error =>
            error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            // The browser response is identical to success either way, so nothing here reveals
            // whether the address is registered. What differs is what lands in the mailbox - which
            // only the address's owner ever sees.
            //
            // The first version of this sent no mail at all, which was a trap: someone who lost the
            // original mail got "check your inbox" and then nothing, for ever, with no way to get a
            // new link. Resending it for an unverified account fixes that without weakening
            // anything; the per-IP rate limit is what stops it being used to pester an address.
            await HandleExistingAccountAsync(email);

            TempData["RegisterMessage"] = CheckYourInboxMessage;
            return RedirectToCurrentUmbracoPage();
        }

        logger.LogWarning(
            "Could not create a member: {Errors}",
            string.Join("; ", created.Errors.Select(error => error.Code)));

        ModelState.AddModelError(string.Empty, messages.Describe(created.Errors.First()));
        return RedisplayForm(form);
    }

    /// <summary>
    /// Decides what the owner of an already-registered address receives.
    /// </summary>
    /// <remarks>
    /// Not yet verified — a fresh verification link, because the likeliest reason somebody is
    /// registering again is that the first mail never arrived or was lost. A new token is generated
    /// rather than the old one reused, so the link works even if the original has expired.
    ///
    /// Already active — a short "you already have an account" note with a login link, so the
    /// response is not a dead end. Only the mailbox owner sees it.
    /// </remarks>
    private async Task HandleExistingAccountAsync(string email)
    {
        MemberIdentityUser? existing = await memberManager.FindByEmailAsync(email);
        if (existing is null)
        {
            // A duplicate that cannot be looked up means the address collided on username rather
            // than email, or it vanished between the two calls. Nothing useful to send.
            logger.LogWarning("A duplicate registration matched no member; no mail sent.");
            return;
        }

        if (existing.IsApproved && existing.EmailConfirmed)
        {
            var loginUrl = PageUrl("login");
            if (loginUrl is null)
            {
                logger.LogError("The login page could not be found; no 'account exists' mail sent.");
                return;
            }

            logger.LogInformation("Re-registration for an active account; sent a login reminder.");
            await mailService.SendAccountAlreadyExistsAsync(existing.Email!, loginUrl);
            return;
        }

        logger.LogInformation("Re-registration for an unverified account; resent the verification link.");
        await SendVerificationMailAsync(existing);
    }

    /// <summary>
    /// Puts everything the member typed back into ViewData so a rejected form comes back filled in.
    /// </summary>
    /// <remarks>
    /// Neither password is carried back: they are the two fields a browser's own password manager
    /// refills, and round-tripping a password through the rendered HTML is not worth doing to save
    /// a keystroke.
    ///
    /// Not carrying the rest back would mean retyping nine fields to fix one typo, which is how
    /// people give up on joining a tennis club.
    /// </remarks>
    private IActionResult RedisplayForm(RegisterFormModel form)
    {
        ViewData["Email"] = form.Email;
        ViewData["FirstName"] = form.FirstName;
        ViewData["LastName"] = form.LastName;
        ViewData["Phone"] = form.Phone;
        ViewData["ChildFirstName"] = form.ChildFirstName;
        ViewData["ChildLastName"] = form.ChildLastName;
        ViewData["ChildBirthDate"] = form.ChildBirthDate;

        return CurrentUmbracoPage();
    }

    /// <summary>
    /// Names the member and stores their phone number.
    /// </summary>
    /// <remarks>
    /// The member's Name is what the backoffice member list shows, and Umbraco defaults it to the
    /// username - which here is the email address. A list of email addresses is not something
    /// anyone can administer, so the guardian's real name replaces it.
    ///
    /// A failure here is logged, not fatal: the account and its verification mail matter more than
    /// the display name, and an administrator can fill it in.
    /// </remarks>
    private async Task SaveGuardianDetailsAsync(Guid memberKey, RegisterFormModel form)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Created member {Key} but could not read it back to name it.", memberKey);
            return;
        }

        member.Name = $"{form.FirstName.Trim()} {form.LastName.Trim()}".Trim();
        member.SetValue(MemberProfileService.PhoneAlias, form.Phone.Trim());
        memberService.Save(member);
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
        var baseUrl = PageUrl("memberVerify");
        if (baseUrl is null)
        {
            return null;
        }

        // The token is base64 with +, / and = in it, so it must be escaped or the query string
        // arrives mangled and confirmation always fails.
        return $"{baseUrl}?member={Uri.EscapeDataString(memberKey.ToString())}"
               + $"&token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// The absolute URL of the first page of a given document type. Absolute because these go into
    /// mail, where a relative path means nothing.
    /// </summary>
    private string? PageUrl(string documentTypeAlias)
        => contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(documentTypeAlias))
            .FirstOrDefault()?
            .Url(publishedUrlProvider, mode: UrlMode.Absolute);

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
