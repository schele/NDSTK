using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Seeds the consent banner's text as Umbraco Dictionary items.
/// </summary>
/// <remarks>
/// Dictionary items are culture-variant regardless of document type variance, which is what lets the
/// banner be bilingual while the content types remain invariant.
/// <para>
/// Every item is filed under a single <see cref="ParentKey" /> node. Umbraco dictionary keys are
/// global rather than path-based, so nesting is presentation only - it keeps 33 items from sitting
/// loose at the root of the Dictionary tree without changing a single lookup.
/// </para>
/// </remarks>
internal sealed class NdstkDictionaryInstaller(
    IDictionaryItemService dictionaryItemService,
    ILanguageService languageService,
    ILogger<NdstkDictionaryInstaller> logger)
{
    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    /// <summary>Parent node for every item below. Holds no translations: it is a folder, not a label.</summary>
    private const string ParentKey = "Cookie.Banner";

    /// <summary>Key, Swedish, English. Swedish first because it is the default language.</summary>
    private static readonly (string Key, string Sv, string En)[] Items =
    [
        ("Cookies.Banner.Heading", "Vi använder kakor", "We use cookies"),
        ("Cookies.Banner.Body",
            "Vi använder nödvändiga kakor för att sajten ska fungera. Vi vill också gärna använda kakor för statistik och innehåll från andra tjänster.",
            "We use necessary cookies to make the site work. We would also like to use cookies for statistics and content from other services."),
        ("Cookies.Banner.AcceptAll", "Godkänn alla", "Accept all"),
        ("Cookies.Banner.RejectAll", "Neka alla", "Reject all"),
        ("Cookies.Banner.Customise", "Anpassa", "Customise"),
        ("Cookies.Banner.Save", "Spara val", "Save choices"),
        ("Cookies.Banner.Cancel", "Avbryt", "Cancel"),
        ("Cookies.Banner.PolicyLink", "Läs mer om kakor", "Read more about cookies"),
        ("Cookies.Banner.Label", "Samtycke till kakor", "Cookie consent"),
        ("Cookies.Banner.Error", "Något gick fel. Försök igen.", "Something went wrong. Please try again."),
        ("Cookies.Banner.RateLimited",
            "Du har försökt för många gånger. Vänta en stund och försök igen.",
            "You've tried too many times. Please wait a moment and try again."),
        ("Cookies.Settings.Heading", "Inställningar för kakor", "Cookie settings"),
        ("Cookies.Category.Necessary.Name", "Nödvändiga", "Necessary"),
        ("Cookies.Category.Necessary.Description",
            "Krävs för att sajten ska fungera, till exempel inloggning. Kan inte stängas av.",
            "Required for the site to work, for example logging in. Cannot be turned off."),
        ("Cookies.Category.Preferences.Name", "Funktionella", "Preferences"),
        ("Cookies.Category.Preferences.Description",
            "Sparar dina val, till exempel språk.",
            "Remembers your choices, such as language."),
        ("Cookies.Category.Statistics.Name", "Statistik", "Statistics"),
        ("Cookies.Category.Statistics.Description",
            "Hjälper oss förstå hur sajten används. Helt anonymt.",
            "Helps us understand how the site is used. Fully anonymous."),
        ("Cookies.Category.Marketing.Name", "Marknadsföring", "Marketing"),
        ("Cookies.Category.Marketing.Description",
            "Används av inbäddat innehåll, till exempel filmer från YouTube.",
            "Used by embedded content, such as YouTube videos."),
        ("Cookies.Category.Cookies", "Kakor i den här kategorin", "Cookies in this category"),
        ("Cookies.Embed.Blocked.Body",
            "Det här innehållet kommer från en annan tjänst och kräver ditt samtycke.",
            "This content comes from another service and needs your consent."),
        ("Cookies.Embed.Blocked.Button", "Visa innehåll", "Show content"),
        ("Cookies.Policy.CurrentChoice", "Ditt nuvarande val", "Your current choice"),
        ("Cookies.Policy.NoChoice", "Du har inte gjort något val än.", "You have not made a choice yet."),
        ("Cookies.Policy.Reopen", "Ändra inställningar", "Change settings"),
        ("Cookies.Policy.Withdraw", "Återkalla samtycke", "Withdraw consent"),
        ("Cookies.Footer.Link", "Cookieinställningar", "Cookie settings"),
        ("Cookies.Table.Name", "Namn", "Name"),
        ("Cookies.Table.Provider", "Leverantör", "Provider"),
        ("Cookies.Table.Purpose", "Syfte", "Purpose"),
        ("Cookies.Table.Duration", "Lagringstid", "Duration"),
        ("Cookies.Table.Type", "Typ", "Type"),
    ];

    public async Task InstallAsync()
    {
        ILanguage? swedish = await languageService.GetAsync("sv");
        ILanguage? english = await languageService.GetAsync("en-GB");

        if (swedish is null)
        {
            logger.LogWarning("Skipping dictionary seeding: the 'sv' language does not exist yet.");
            return;
        }

        Guid? parentId = await EnsureParentAsync();

        var created = 0;
        var adopted = 0;
        foreach ((string key, string sv, string en) in Items)
        {
            IDictionaryItem? existing = await dictionaryItemService.GetAsync(key);
            if (existing is not null)
            {
                if (await TryAdoptAsync(existing, parentId))
                {
                    adopted++;
                }

                continue;
            }

            var translations = new List<IDictionaryTranslation>
            {
                new DictionaryTranslation(swedish, sv),
            };

            if (english is not null)
            {
                translations.Add(new DictionaryTranslation(english, en));
            }

            var item = new DictionaryItem(parentId, key) { Translations = translations };

            var attempt = await dictionaryItemService.CreateAsync(item, UserKey);
            if (attempt.Success is false)
            {
                logger.LogWarning("Could not create dictionary item {Key}: {Status}.", key, attempt.Status);
                continue;
            }

            created++;
        }

        if (created > 0)
        {
            logger.LogInformation("Seeded {Count} cookie dictionary items.", created);
        }

        if (adopted > 0)
        {
            logger.LogInformation(
                "Filed {Count} existing cookie dictionary items under '{Parent}'.", adopted, ParentKey);
        }
    }

    /// <summary>
    /// Returns the id of the parent node, creating it if absent. Returns null when it cannot be
    /// created: seeding the text still matters more than where the items sit in the tree.
    /// </summary>
    private async Task<Guid?> EnsureParentAsync()
    {
        IDictionaryItem? existing = await dictionaryItemService.GetAsync(ParentKey);
        if (existing is not null)
        {
            return existing.Key;
        }

        // No translations. The tree labels a node by its key, so this reads as "Cookie.Banner" while
        // staying invisible to Umbraco.GetDictionaryValue - nothing renders it.
        var parent = new DictionaryItem(ParentKey) { Translations = [] };

        var attempt = await dictionaryItemService.CreateAsync(parent, UserKey);
        if (attempt.Success is false)
        {
            logger.LogWarning(
                "Could not create the '{Parent}' dictionary parent: {Status}. Items stay at the root.",
                ParentKey,
                attempt.Status);
            return null;
        }

        return attempt.Result?.Key;
    }

    /// <summary>
    /// Files an item that is still at the root under the parent - the one-off tidy for items seeded
    /// before this grouping existed. An item an editor has deliberately moved somewhere else is left
    /// where they put it: this seeder creates and tidies, it does not enforce a shape on every boot.
    /// </summary>
    private async Task<bool> TryAdoptAsync(IDictionaryItem item, Guid? parentId)
    {
        if (parentId is null || item.ParentId is not null)
        {
            return false;
        }

        var attempt = await dictionaryItemService.MoveAsync(item, parentId, UserKey);
        if (attempt.Success)
        {
            return true;
        }

        logger.LogWarning(
            "Could not file dictionary item {Key} under '{Parent}': {Status}.",
            item.ItemKey,
            ParentKey,
            attempt.Status);
        return false;
    }
}
