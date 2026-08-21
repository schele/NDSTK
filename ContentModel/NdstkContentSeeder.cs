using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Fills a brand new site with the start page from the previous NDSTK build - hero, news list,
/// sidebar widgets, settings and a few articles - so the design is visible without any manual
/// setup. Runs only while the content tree is completely empty, so it never touches a site that
/// already has content.
/// </summary>
internal sealed class NdstkContentSeeder(
    IContentService contentService,
    IJsonSerializer jsonSerializer,
    ILogger<NdstkContentSeeder> logger)
{
    // IContentService still only takes an integer user id, so the obsolete constant is the only
    // option here. Swap to SuperUserKey once the content service exposes key-based overloads.
#pragma warning disable CS0618
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    public void Seed()
    {
        if (contentService.GetRootContent().Any())
        {
            return;
        }

        logger.LogInformation("Content tree is empty - seeding the NDSTK start page.");

        IContent start = Create("Start", -1, "start", Nodes.Start);
        contentService.Save(start, UserId);

        IContent articles = Create("Articles", start.Id, "articles", Nodes.Articles);
        contentService.Save(articles, UserId);

        IContent[] posts = SeedArticles(articles);

        IContent login = Create("Logga in", start.Id, "login", Nodes.Login);
        login.SetValue("heading", "Logga in med BankID");
        login.SetValue("description", "Skanna QR-koden med BankID-appen för att logga in som medlem.");
        login.SetValue("subText", "Väntar på BankID...");
        contentService.Save(login, UserId);

        IContent error = Create("404", start.Id, "error", Nodes.Error);
        error.SetValue("heading", "Sidan kan inte hittas");
        error.SetValue("text", "Sidan du letar efter finns inte längre. Prova startsidan istället.");
        error.SetValue("metaRobots", Dropdown("NOINDEX,NOFOLLOW"));
        contentService.Save(error, UserId);

        IContent settings = SeedSettings(start, articles, login);
        SeedStartPage(start, articles);

        // Publishing is strictly top down: Umbraco refuses to publish a node whose ancestors
        // are still unpublished.
        foreach (IContent node in new[] { start, settings, articles, login, error }.Concat(posts))
        {
            Publish(node);
        }

        logger.LogInformation("NDSTK start page seeded.");
    }

    // ------------------------------------------------------------------ nodes

    private IContent[] SeedArticles(IContent parent)
    {
        (string Name, DateTime Date, string Summary)[] posts =
        [
            ("New Season Kickoff Event", new DateTime(2025, 11, 3),
                "Join us for the NDSTK season kickoff this Saturday! Matches, fun games, and prizes await all members."),
            ("Summer Training Sessions Open", new DateTime(2025, 10, 20),
                "We’ve opened registration for our summer sessions — all levels welcome! Reserve your spot early."),
            ("Congrats to Our Doubles Champions", new DateTime(2025, 10, 10),
                "Huge congratulations to our doubles champions — a fantastic display of skill and teamwork."),
        ];

        List<IContent> created = [];
        foreach ((string name, DateTime date, string summary) in posts)
        {
            IContent article = contentService.Create(name, parent.Id, "article", UserId);
            article.SetValue("heading", name);
            article.SetValue("publishDate", date);
            article.SetValue("summary", summary);
            article.SetValue("body", $"<p>{summary}</p>");
            article.SetValue("tags", Tags("Tennis", "Events"));
            contentService.Save(article, UserId);
            created.Add(article);
        }

        return [.. created];
    }

    private IContent SeedSettings(IContent start, IContent articles, IContent login)
    {
        IContent settings = Create("Settings", start.Id, "settings", Nodes.Settings);
        settings.SetValue("siteName", "NDSTK");
        settings.SetValue("menu", NodeList(articles));
        settings.SetValue("loginPage", Node(login));
        settings.SetValue("footerText", $"© {DateTime.UtcNow.Year} NDSTK Tennis Club — Serve. Volley. Repeat.");
        settings.SetValue("sidebarWidgets", BlockList(
            Block(ElementTypes.CtaWidget,
                ("heading", "Bli medlem i NDSTK"),
                ("text", "Bli medlem och stöd det lokala föreningslivet. Kostnaden är endast 200 sek. De 20 första medlemmarna får en gratis t-shirt."),
                ("link", Link("Bli medlem", "#members")),
                ("highlight", 1)),
            Block(ElementTypes.ContactWidget,
                ("heading", "Kontakt"),
                ("email", "info@ndstk.se")),
            Block(ElementTypes.TagsWidget,
                ("heading", "Tags"),
                ("tags", Tags("Tennis", "Training", "Events", "Community", "Competition")))));

        contentService.Save(settings, UserId);
        return settings;
    }

    private void SeedStartPage(IContent start, IContent articles)
    {
        start.SetValue("metaTitle", "NDSTK - Tennisklubben i Norra Djurgårdsstaden");
        start.SetValue("metaDescription", "Norra Djurgårdsstadens Tennisklubb - bli medlem och stöd det lokala föreningslivet.");
        start.SetValue("metaRobots", Dropdown("INDEX,FOLLOW"));
        start.SetValue("contentBlocks", BlockList(
            Block(ElementTypes.Hero,
                ("heading", "Norra Djurgårdsstadens Tennisklubb"),
                ("text", "Bli medlem och stöd det lokala föreningslivet. Kostnaden är endast 200 sek. De 20 första medlemmarna får en t-shirt gratis."),
                ("link", Link("Bli medlem", "#members"))),
            Block(ElementTypes.NewsList,
                ("source", Node(articles)),
                ("maxItems", 3))));

        contentService.Save(start, UserId);
    }

    // ------------------------------------------------------------- primitives

    private IContent Create(string name, int parentId, string contentTypeAlias, Guid key)
    {
        IContent content = contentService.Create(name, parentId, contentTypeAlias, UserId);
        content.Key = key;
        return content;
    }

    private void Publish(IContent content)
    {
        PublishResult result = contentService.Publish(content, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning("Could not publish '{Name}': {Status}.", content.Name, result.Result);
        }
    }

    /// <summary>Content picker values are stored as a single UDI.</summary>
    private static string Node(IContent content) => Udi.Create(Constants.UdiEntityType.Document, content.Key).ToString();

    /// <summary>Multi node tree picker values are stored as a comma separated list of UDIs.</summary>
    private static string NodeList(params IContent[] content) => string.Join(",", content.Select(Node));

    private string Tags(params string[] tags) => jsonSerializer.Serialize(tags);

    /// <summary>The flexible dropdown always stores an array, even in single-value mode.</summary>
    private string Dropdown(string value) => jsonSerializer.Serialize(new[] { value });

    /// <summary>A Multi URL Picker entry for an external / anchor link.</summary>
    private string Link(string name, string url) => jsonSerializer.Serialize(new[]
    {
        new Dictionary<string, object?>
        {
            ["name"] = name,
            ["url"] = url,
            ["type"] = "external",
            ["target"] = null,
        },
    });

    private static BlockItemData Block(Guid elementTypeKey, params (string Alias, object Value)[] values)
        => new()
        {
            Key = Guid.NewGuid(),
            ContentTypeKey = elementTypeKey,
            Values = values
                .Select(value => new BlockPropertyValue { Alias = value.Alias, Value = value.Value })
                .Cast<BlockPropertyValue>()
                .ToList(),
        };

    /// <summary>
    /// Assembles the Block List property value: the layout referencing each block, the block
    /// content itself, and the "expose" list that marks the blocks as visible.
    /// </summary>
    private string BlockList(params BlockItemData[] blocks)
    {
        var value = new BlockListValue
        {
            Layout = new Dictionary<string, IEnumerable<IBlockLayoutItem>>
            {
                [Constants.PropertyEditors.Aliases.BlockList] =
                    blocks.Select(block => new BlockListLayoutItem(block.Key)).ToArray(),
            },
            ContentData = [.. blocks],
            SettingsData = [],
            Expose = blocks.Select(block => new BlockItemVariation(block.Key, null, null)).ToList(),
        };

        return jsonSerializer.Serialize(value);
    }
}
