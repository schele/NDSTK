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
/// setup. The full seed runs only while the content tree is completely empty, so it never rewrites
/// a site that already has content. Against an already-installed site it does exactly one thing -
/// see <see cref="EnsureCookiePolicyPage"/> - and touches nothing else.
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
            // The tree already has content from before this feature existed, so none of the
            // full first-run seeding below applies - but the site still needs somewhere for the
            // consent banner's "read more" link to point at. This is the only part of an
            // already-installed site this seeder ever touches.
            EnsureCookiePolicyPage();
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

        IContent cookiePolicy = SeedCookiePolicy(start);

        IContent settings = SeedSettings(start, articles, login, cookiePolicy);
        SeedStartPage(start, articles);

        // Publishing is strictly top down: Umbraco refuses to publish a node whose ancestors
        // are still unpublished.
        foreach (IContent node in new[] { start, settings, articles, login, error, cookiePolicy }.Concat(posts))
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

    private IContent SeedSettings(IContent start, IContent articles, IContent login, IContent cookiePolicy)
    {
        IContent settings = Create("Settings", start.Id, "settings", Nodes.Settings);
        settings.SetValue("siteName", "NDSTK");
        settings.SetValue("menu", NodeList(articles));
        settings.SetValue("loginPage", Node(login));
        settings.SetValue("cookiePolicyPage", Node(cookiePolicy));
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

    /// <summary>
    /// Runs on every start against a site that already has content, so an install that predates
    /// this feature does not end up with a consent banner and no page for it to link to. Idempotent:
    /// once the page exists, this just finds it by key and does nothing on every later restart.
    /// </summary>
    private void EnsureCookiePolicyPage()
    {
        if (contentService.GetById(Nodes.CookiePolicy) is not null)
        {
            return;
        }

        IContent? root = contentService.GetRootContent().FirstOrDefault();
        if (root is null)
        {
            // Nothing to parent it to. Should not happen given the caller only reaches here when
            // GetRootContent().Any() was true, but a stale/mutated read is not worth a crash.
            return;
        }

        IContent policy = SeedCookiePolicy(root);
        Publish(policy);

        logger.LogInformation(
            "Created and published the cookie policy page '{Name}' under '{Root}' for an already-installed site.",
            policy.Name, root.Name);
    }

    private IContent SeedCookiePolicy(IContent start)
    {
        IContent policy = Create("Cookies", start.Id, "cookiePolicy", Nodes.CookiePolicy);
        policy.SetValue("heading", "Kakor på ndstk.se");
        policy.SetValue("introduction",
            "<p>Vi använder kakor (cookies) för att sajten ska fungera. Nedan ser du exakt vilka kakor vi " +
            "sätter, varför, och hur länge de sparas.</p>");
        policy.SetValue("outro",
            "<p>Du kan även blockera och radera kakor i din webbläsares inställningar. Har du frågor, " +
            "kontakta oss på <a href=\"mailto:info@ndstk.se\">info@ndstk.se</a>. Du kan läsa mer om kakor " +
            "hos <a href=\"https://www.imy.se/\">Integritetsskyddsmyndigheten</a>.</p>");

        // Only what this site genuinely sets today. An invented table would be worse than a short one.
        policy.SetValue("cookies", BlockList(
            Block(ElementTypes.CookieDefinition,
                ("cookieName", "ndstk-consent"),
                ("provider", "NDSTK"),
                ("category", Dropdown("necessary")),
                ("purpose", "Sparar ditt val av kakor så att vi inte behöver fråga igen."),
                ("duration", "12 månader"),
                ("storageType", Dropdown("Cookie"))),
            Block(ElementTypes.CookieDefinition,
                ("cookieName", ".AspNetCore.Antiforgery.*"),
                ("provider", "NDSTK"),
                ("category", Dropdown("necessary")),
                ("purpose", "Skyddar formulär mot förfalskade anrop."),
                ("duration", "Session"),
                ("storageType", Dropdown("Cookie"))),
            Block(ElementTypes.CookieDefinition,
                ("cookieName", "UMB_MEMBER"),
                ("provider", "NDSTK"),
                ("category", Dropdown("necessary")),
                ("purpose", "Håller dig inloggad som medlem efter inloggning med BankID."),
                ("duration", "Session"),
                ("storageType", Dropdown("Cookie")))));

        contentService.Save(policy, UserId);
        return policy;
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
