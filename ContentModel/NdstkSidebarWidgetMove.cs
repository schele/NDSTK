using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Carries the sidebar widgets from the Settings node to the start page, and drops the field they
/// came from.
/// </summary>
/// <remarks>
/// The schema half of this move is in <see cref="NdstkContentModelInstaller"/>; only the content is
/// here. The order between the two matters and is not obvious: the field has to exist on the start
/// page before anything can be written to it, and the field on Settings must not be dropped until
/// after its contents have been carried across - dropping a property type takes every value stored
/// against it with it.
///
/// Guarded by a marker in the key/value store and run once, the same pattern
/// <see cref="NdstkMemberContentUpgrade"/> uses, because it writes over content an editor also owns.
/// It runs after that upgrade, too, which is what lets it stay this simple: the duplicate "Bli
/// medlem" widget is already gone from Settings by the time this reads it, so there is nothing to
/// filter out here.
/// </remarks>
internal sealed class NdstkSidebarWidgetMove(
    NdstkContentTypeFactory factory,
    IContentService contentService,
    IJsonSerializer jsonSerializer,
    IKeyValueService keyValueService,
    ILogger<NdstkSidebarWidgetMove> logger)
{
    private const string StateKey = "NDSTK/SidebarWidgetMove";
    private const string StateValue = "settings-to-start-v1";

    private const string Widgets = "sidebarWidgets";

    /// <summary>The Block List stores its ordering under the property editor's own alias.</summary>
    private const string LayoutKey = Constants.PropertyEditors.Aliases.BlockList;

#pragma warning disable CS0618 // IContentService still only takes an integer user id.
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    public async Task RunAsync()
    {
        if (keyValueService.GetValue(StateKey) == StateValue)
        {
            return;
        }

        IContent? start = FindStartPage();
        if (start is null)
        {
            // A database with no content in it yet. The seeder puts the widgets straight onto the
            // start page, so there will be nothing to do next boot either - but the marker stays
            // unset rather than claiming a move that never ran.
            return;
        }

        if (start.HasProperty(Widgets) is false)
        {
            // The schema half did not land. Left unmarked deliberately, so this is retried once the
            // installer has managed to add the field rather than being written off as done.
            logger.LogWarning(
                "The start page has no '{Property}' field yet, so the sidebar widgets stay on Settings.",
                Widgets);
            return;
        }

        if (Carry(start) is false)
        {
            return;
        }

        // Only now that the widgets are somewhere else. See the remarks on RemovePropertyAsync:
        // this is the one destructive thing the factory does.
        if (await factory.RemovePropertyAsync(DocumentTypes.Settings, Widgets))
        {
            logger.LogInformation("Removed the sidebar widgets field from the settings document type.");
        }

        keyValueService.SetValue(StateKey, StateValue);
    }

    /// <summary>
    /// Writes the widgets onto the start page.
    /// </summary>
    /// <returns>
    /// False when the caller should stop and try again on the next boot, leaving the field on
    /// Settings alone.
    /// </returns>
    private bool Carry(IContent start)
    {
        // An editor who has already arranged the sidebar on the start page - or a site the seeder
        // has just filled in - is left exactly as it is. This is the guard that makes a re-run
        // harmless if the marker is ever lost.
        if (string.IsNullOrWhiteSpace(start.GetValue<string>(Widgets)) is false)
        {
            logger.LogInformation("The start page already has sidebar widgets; nothing to carry.");
            return true;
        }

        // Read before the value is written, because writing it is itself an edit. A start page with
        // work in progress on it must not be published by an installer: that would put somebody's
        // unfinished draft live as a side effect of an upgrade.
        var hasDraft = start.Edited;

        IContent? settings = contentService.GetById(Nodes.Settings);
        BlockListValue widgets = Read(settings?.GetValue<string>(Widgets));

        // Where the box used to be. It was hardcoded above the widget list in Root.cshtml, so on
        // every site that has run this it sat first, and a member's way in is the one box in the
        // sidebar worth putting at the top.
        PrependMemberWidget(widgets);

        start.SetValue(Widgets, jsonSerializer.Serialize(widgets));
        contentService.Save(start, UserId);

        logger.LogInformation(
            "Carried {Count} sidebar widget(s) onto the start page.", widgets.ContentData.Count);

        if (hasDraft)
        {
            logger.LogWarning(
                "The start page has unpublished changes, so the sidebar was saved but not published. "
                + "Publish the start page to bring the widgets back to the site.");
            return true;
        }

        PublishResult result = contentService.Publish(start, AllCultures, UserId);
        if (result.Success)
        {
            return true;
        }

        // Not marked done: the widgets are saved but the site is showing an empty sidebar, and the
        // field on Settings is still the only published copy. Worth another attempt.
        logger.LogError(
            "Could not publish the start page after moving the sidebar widgets: {Status}.", result.Result);
        return false;
    }

    private void PrependMemberWidget(BlockListValue widgets)
    {
        var block = new BlockItemData
        {
            Key = Guid.NewGuid(),
            ContentTypeKey = ElementTypes.MemberWidget,
            Values = [new BlockPropertyValue { Alias = "heading", Value = "Medlem" }],
        };

        // Three places, as ever with a Block List: the content, the layout that orders it, and the
        // expose list that marks it visible. Leaving it out of any one of them leaves a block that
        // does not render, or an orphan Umbraco logs a warning about.
        widgets.ContentData.Insert(0, block);
        widgets.Expose.Insert(0, new BlockItemVariation(block.Key, null, null));
        widgets.Layout[LayoutKey] =
        [
            new BlockListLayoutItem(block.Key),
            .. widgets.Layout.TryGetValue(LayoutKey, out IEnumerable<IBlockLayoutItem>? ordered)
                ? ordered
                : [],
        ];
    }

    /// <summary>The stored value, or an empty Block List when there is nothing to carry.</summary>
    private BlockListValue Read(string? raw)
        => (string.IsNullOrWhiteSpace(raw) ? null : jsonSerializer.Deserialize<BlockListValue>(raw))
           ?? new BlockListValue
           {
               Layout = new Dictionary<string, IEnumerable<IBlockLayoutItem>>(),
               ContentData = [],
               SettingsData = [],
               Expose = [],
           };

    /// <summary>
    /// The seeded start page by key, falling back to whatever is at the root. The fallback is worth
    /// having: getting this wrong empties the sidebar on every page of the site, and a root node
    /// created by hand rather than by the seeder would not carry the key.
    /// </summary>
    private IContent? FindStartPage()
        => contentService.GetById(Nodes.Start)
           ?? contentService.GetRootContent()
               .FirstOrDefault(node => node.ContentType.Alias == "start");
}
