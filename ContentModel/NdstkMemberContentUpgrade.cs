using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Serialization;
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
    IJsonSerializer jsonSerializer,
    IKeyValueService keyValueService,
    ILogger<NdstkMemberContentUpgrade> logger)
{
    private const string StateKey = "NDSTK/MemberAreaContent";
    // Bumped whenever the upgrade learns to do something new, so it runs once more: v2 filled in the
    // portal picker once that page existed, v3 repointed the dead calls to action, v4 removes the
    // duplicate "Bli medlem" sidebar widget.
    private const string StateValue = "login-copy+settings-pickers+cta-targets+no-join-widget-v4";

    /// <summary>The placeholder the previous design's calls to action pointed at.</summary>
    private const string DeadAnchor = "#members";

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
        RepointCallsToAction(register);
        RemoveJoinCallToActionWidget();

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

    /// <summary>
    /// Points the seeded "Bli medlem" buttons at the registration page.
    /// </summary>
    /// <remarks>
    /// The previous design's calls to action linked to <c>#members</c>, an anchor that never
    /// existed. There are two: the hero on the start page and the sidebar widget on Settings. Both
    /// live inside Block List JSON, so the value has to be deserialised, walked and written back
    /// rather than patched as text.
    ///
    /// The replacement is a <em>document</em> link rather than the path "/bli-medlem/", so renaming
    /// or moving the page in the backoffice does not break the button again.
    /// </remarks>
    private void RepointCallsToAction(IContent? register)
    {
        if (register is null)
        {
            return;
        }

        (Guid NodeKey, string Property)[] targets =
        [
            (Nodes.Start, "contentBlocks"),
            (Nodes.Settings, "sidebarWidgets"),
        ];

        foreach ((Guid nodeKey, string property) in targets)
        {
            IContent? node = contentService.GetById(nodeKey);
            if (node is null || RepointBlockList(node, property, register) is false)
            {
                continue;
            }

            contentService.Save(node, UserId);
            Publish(node);
            logger.LogInformation(
                "Repointed the '{Property}' call(s) to action on '{Name}' at the registration page.",
                property, node.Name);
        }
    }

    /// <summary>
    /// Removes the seeded "Bli medlem i NDSTK" widget from the sidebar.
    /// </summary>
    /// <remarks>
    /// It duplicated the call to action already in the hero on the start page, and once a visitor is
    /// signed in it is simply wrong - inviting somebody to join who already has. The hero keeps the
    /// club's public invitation, so nothing is lost.
    ///
    /// Removing a Block List entry means taking it out of three places: the content itself, the
    /// layout that orders it, and the expose list that marks it visible. Leaving it in any one of
    /// them leaves the value inconsistent, and Umbraco will either still render the block or log a
    /// warning about an orphan.
    /// </remarks>
    private void RemoveJoinCallToActionWidget()
    {
        IContent? settings = contentService.GetById(Nodes.Settings);
        if (settings is null)
        {
            return;
        }

        var raw = settings.GetValue<string>("sidebarWidgets");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        BlockListValue? blocks = jsonSerializer.Deserialize<BlockListValue>(raw);
        if (blocks is null)
        {
            return;
        }

        HashSet<Guid> doomed =
        [
            .. blocks.ContentData
                .Where(block => block.ContentTypeKey == ElementTypes.CtaWidget)
                .Select(block => block.Key),
        ];

        if (doomed.Count == 0)
        {
            return;
        }

        blocks.ContentData.RemoveAll(block => doomed.Contains(block.Key));
        blocks.Expose.RemoveAll(item => doomed.Contains(item.ContentKey));

        foreach (var layout in blocks.Layout.ToList())
        {
            blocks.Layout[layout.Key] =
                layout.Value.Where(item => doomed.Contains(item.ContentKey) is false).ToArray();
        }

        settings.SetValue("sidebarWidgets", jsonSerializer.Serialize(blocks));
        contentService.Save(settings, UserId);
        Publish(settings);

        logger.LogInformation(
            "Removed {Count} 'Bli medlem' sidebar widget(s) from the settings node.", doomed.Count);
    }

    private bool RepointBlockList(IContent node, string propertyAlias, IContent register)
    {
        var raw = node.GetValue<string>(propertyAlias);

        // Cheap guard first: most sites will not contain the placeholder at all.
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains(DeadAnchor, StringComparison.Ordinal) is false)
        {
            return false;
        }

        BlockListValue? blocks = jsonSerializer.Deserialize<BlockListValue>(raw);
        if (blocks is null)
        {
            logger.LogWarning("Could not read '{Property}' on '{Name}'.", propertyAlias, node.Name);
            return false;
        }

        var changed = false;

        foreach (BlockPropertyValue value in blocks.ContentData.SelectMany(block => block.Values))
        {
            if (value.Alias != "link")
            {
                continue;
            }

            var link = value.Value?.ToString();
            if (link is null || link.Contains(DeadAnchor, StringComparison.Ordinal) is false)
            {
                continue;
            }

            value.Value = DocumentLink("Bli medlem", register.Key);
            changed = true;
        }

        if (changed is false)
        {
            return false;
        }

        node.SetValue(propertyAlias, jsonSerializer.Serialize(blocks));
        return true;
    }

    /// <summary>
    /// A Multi URL Picker entry pointing at a content node. The property names match
    /// MultiUrlPickerValueEditor.LinkDto, which is what the editor reads back.
    /// </summary>
    private string DocumentLink(string name, Guid nodeKey) => jsonSerializer.Serialize(new[]
    {
        new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = "document",
            ["udi"] = Udi.Create(Constants.UdiEntityType.Document, nodeKey).ToString(),
        },
    });

    private void Publish(IContent content)
    {
        PublishResult result = contentService.Publish(content, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning("Could not publish '{Name}': {Status}.", content.Name, result.Result);
        }
    }
}
