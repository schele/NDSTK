using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// One-off content fixes the member area needs on a site that was seeded before it existed.
/// </summary>
/// <remarks>
/// Guarded by a marker in the key/value store and run exactly once, the same pattern
/// <see cref="NdstkLanguageInstaller"/> uses. That guard is the point: these overwrite values an
/// editor can also change, so without it every restart would undo their work. Everything else in
/// the installer is create-if-missing and needs no such guard.
/// </remarks>
internal sealed class NdstkMemberContentUpgrade(
    IContentService contentService,
    IKeyValueService keyValueService,
    ILogger<NdstkMemberContentUpgrade> logger)
{
    private const string StateKey = "NDSTK/MemberAreaContent";
    // Bumped when the upgrade learns to do something new: the portal page did not exist on the
    // first run, so memberPortalPage could not be filled in then.
    private const string StateValue = "login-copy+settings-pickers-v2";

#pragma warning disable CS0618 // IContentService still only takes an integer user id.
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    public void Upgrade()
    {
        if (keyValueService.GetValue(StateKey) == StateValue)
        {
            return;
        }

        IContent? register = contentService.GetById(Nodes.MemberRegister);
        IContent? portal = contentService.GetById(Nodes.MemberPortal);

        ReplaceBankIdCopy();
        PointSettingsAtMemberPages(register, portal);

        keyValueService.SetValue(StateKey, StateValue);
        logger.LogInformation("Member area content upgrade applied.");
    }

    /// <summary>
    /// The login page was seeded with BankID placeholder copy from the previous design - "Skanna
    /// QR-koden med BankID-appen" - which is now actively wrong, since login is email and password.
    /// </summary>
    private void ReplaceBankIdCopy()
    {
        IContent? login = contentService.GetById(Nodes.Login);
        if (login is null)
        {
            return;
        }

        login.SetValue("heading", "Logga in");
        login.SetValue("description", "Logga in med din e-postadress för att boka träningar.");
        login.SetValue("subText", string.Empty);

        contentService.Save(login, UserId);
        Publish(login);
    }

    /// <summary>
    /// Fills in the Settings pickers so the login redirect and the "Bli medlem" buttons have
    /// somewhere to go without an editor having to wire them by hand.
    /// </summary>
    private void PointSettingsAtMemberPages(IContent? register, IContent? portal)
    {
        IContent? settings = contentService.GetById(Nodes.Settings);
        if (settings is null)
        {
            return;
        }

        var changed = false;

        if (register is not null && string.IsNullOrWhiteSpace(settings.GetValue<string>("registerPage")))
        {
            settings.SetValue("registerPage", Udi.Create(Constants.UdiEntityType.Document, register.Key).ToString());
            changed = true;
        }

        if (portal is not null && string.IsNullOrWhiteSpace(settings.GetValue<string>("memberPortalPage")))
        {
            settings.SetValue("memberPortalPage", Udi.Create(Constants.UdiEntityType.Document, portal.Key).ToString());
            changed = true;
        }

        if (changed is false)
        {
            return;
        }

        contentService.Save(settings, UserId);
        Publish(settings);
    }

    private void Publish(IContent content)
    {
        PublishResult result = contentService.Publish(content, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning("Could not publish '{Name}': {Status}.", content.Name, result.Result);
        }
    }
}
