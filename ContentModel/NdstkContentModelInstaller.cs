using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Declares the NDSTK content model - templates, Block List blocks, data types and document
/// types - and creates whatever is missing. It runs after boot on every start; because each step
/// is create-if-missing it is cheap on an installed site and self-healing on a fresh database.
/// </summary>
internal sealed class NdstkContentModelInstaller(
    NdstkContentTypeFactory factory,
    NdstkLanguageInstaller languages,
    NdstkDictionaryInstaller dictionary,
    IHostEnvironment hostEnvironment,
    ILogger<NdstkContentModelInstaller> logger)
{
    public async Task InstallAsync()
    {
        await languages.InstallAsync();
        // Dictionary translations reference the sv and en-GB ILanguage objects the language installer creates,
        // so this must run after languages are configured.
        await dictionary.InstallAsync();

        Dictionary<Guid, ITemplate> templates = await InstallTemplatesAsync();

        // The built-in data types the blocks and pages bind to.
        await factory.PreloadDataTypesAsync(
            BuiltInDataTypes.Textstring,
            BuiltInDataTypes.Textarea,
            BuiltInDataTypes.RichtextEditor,
            BuiltInDataTypes.Tags,
            BuiltInDataTypes.MultiUrlPicker,
            BuiltInDataTypes.DatePicker,
            BuiltInDataTypes.TrueFalse,
            BuiltInDataTypes.ImageMediaPicker,
            BuiltInDataTypes.ContentPicker,
            BuiltInDataTypes.Numeric);

        // The cookie category / storage type dropdowns must exist - and be preloaded - before
        // InstallElementTypesAsync, because the cookie definition element type binds to them and
        // factory.Property throws if a data type was not preloaded first.
        await InstallCookieDataTypesAsync();
        await factory.PreloadDataTypesAsync(DataTypes.CookieCategory, DataTypes.StorageType);

        await InstallElementTypesAsync();
        await InstallDataTypesAsync();
        await InstallDocumentTypesAsync(templates);

        logger.LogInformation("NDSTK content model is up to date.");
    }

    // ---------------------------------------------------------------- templates

    /// <summary>
    /// Registers a template record for each view that lives in /Views. The physical file is the
    /// source of truth, so its current content is read and handed to Umbraco - otherwise the
    /// template repository would write an empty .cshtml over the real one.
    /// </summary>
    private async Task<Dictionary<Guid, ITemplate>> InstallTemplatesAsync()
    {
        (Guid Key, string Name, string Alias)[] definitions =
        [
            (Templates.Root, "Root", "Root"),
            (Templates.Start, "Start", "Start"),
            (Templates.Article, "Article", "Article"),
            (Templates.Error, "Error", "Error"),
            (Templates.Login, "Login", "Login"),
            (Templates.CookiePolicy, "Cookie policy", "CookiePolicy"),
        ];

        Dictionary<Guid, ITemplate> templates = [];
        foreach ((Guid key, string name, string alias) in definitions)
        {
            templates[key] = await factory.EnsureTemplateAsync(key, name, alias, ReadViewFile(alias));
        }

        return templates;
    }

    private string ReadViewFile(string alias)
    {
        var path = Path.Combine(hostEnvironment.ContentRootPath, "Views", $"{alias}.cshtml");
        if (System.IO.File.Exists(path))
        {
            return System.IO.File.ReadAllText(path);
        }

        logger.LogWarning("View file {Path} is missing; registering an empty template for '{Alias}'.", path, alias);
        return "@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage" + Environment.NewLine;
    }

    // ---------------------------------------------------- cookie registry data types

    /// <summary>
    /// The two dropdowns the cookie definition element type binds to. These have to be created -
    /// and preloaded - before <see cref="InstallElementTypesAsync"/> runs, which is earlier than
    /// data types are normally installed; see the ordering note in <see cref="InstallAsync"/>.
    /// </summary>
    private async Task InstallCookieDataTypesAsync()
    {
        // These are the wire names from ConsentCategories.ToWireName, not display labels: the
        // cookie policy page groups declared cookies by this value, so it must match exactly.
        // Display names come from Umbraco dictionary items, added in a later task.
        await factory.EnsureDataTypeAsync(
            DataTypes.CookieCategory,
            "NDSTK - Cookie category",
            Constants.PropertyEditors.Aliases.DropDownListFlexible,
            "Umb.PropertyEditorUi.Dropdown",
            new Dictionary<string, object>
            {
                ["multiple"] = false,
                ["items"] = new[] { "necessary", "preferences", "statistics", "marketing" },
            });

        await factory.EnsureDataTypeAsync(
            DataTypes.StorageType,
            "NDSTK - Storage type",
            Constants.PropertyEditors.Aliases.DropDownListFlexible,
            "Umb.PropertyEditorUi.Dropdown",
            new Dictionary<string, object>
            {
                ["multiple"] = false,
                ["items"] = new[] { "Cookie", "localStorage", "sessionStorage", "Pixel" },
            });
    }

    // ------------------------------------------------------------ element types

    /// <summary>
    /// Element types come first: the Block List data types below reference them by key. Each one
    /// maps 1:1 onto a partial in /Views/Partials/blocklist/Components.
    /// </summary>
    private async Task InstallElementTypesAsync()
    {
        await EnsureElementTypeAsync(ElementTypes.Hero, "heroBlock", "Hero", "icon-bullhorn",
            "The dark blue intro panel at the top of a page.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.Textarea, "text", "Text", sortOrder: 1),
            factory.Property(BuiltInDataTypes.MultiUrlPicker, "link", "Button", "Rendered as the yellow call-to-action button.", 2),
            factory.Property(BuiltInDataTypes.ImageMediaPicker, "backgroundImage", "Background image", sortOrder: 3));

        await EnsureElementTypeAsync(ElementTypes.Post, "postBlock", "News post", "icon-newspaper",
            "A single hand-written news card.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.DatePicker, "date", "Date", sortOrder: 1),
            factory.Property(BuiltInDataTypes.Textarea, "text", "Text", sortOrder: 2),
            factory.Property(BuiltInDataTypes.MultiUrlPicker, "link", "Link", sortOrder: 3));

        await EnsureElementTypeAsync(ElementTypes.NewsList, "newsListBlock", "News list", "icon-list",
            "Lists the most recent articles from an Articles folder.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.ContentPicker, "source", "Articles folder", "The node to list articles from.", 1),
            factory.Property(BuiltInDataTypes.Numeric, "maxItems", "Number of articles", "Defaults to 3 when left empty.", 2));

        await EnsureElementTypeAsync(ElementTypes.Text, "textBlock", "Rich text", "icon-article",
            "Free text on a white card.",
            factory.Property(BuiltInDataTypes.RichtextEditor, "body", "Body", sortOrder: 0));

        await EnsureElementTypeAsync(ElementTypes.CtaWidget, "ctaWidgetBlock", "Widget: Call to action", "icon-hand-pointer",
            "Sidebar box with a button, optionally on the brand blue background.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.Textarea, "text", "Text", sortOrder: 1),
            factory.Property(BuiltInDataTypes.MultiUrlPicker, "link", "Button", sortOrder: 2),
            factory.Property(BuiltInDataTypes.TrueFalse, "highlight", "Use brand background", sortOrder: 3));

        await EnsureElementTypeAsync(ElementTypes.ContactWidget, "contactWidgetBlock", "Widget: Contact", "icon-message",
            "Sidebar box with contact details.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.Textstring, "email", "E-mail", sortOrder: 1),
            factory.Property(BuiltInDataTypes.Textstring, "phone", "Phone", sortOrder: 2));

        await EnsureElementTypeAsync(ElementTypes.TagsWidget, "tagsWidgetBlock", "Widget: Tags", "icon-tags",
            "Sidebar box with the tag pills.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
            factory.Property(BuiltInDataTypes.Tags, "tags", "Tags", sortOrder: 1));

        await EnsureElementTypeAsync(ElementTypes.CookieDefinition, "cookieDefinition", "Cookie", "icon-lock",
            "One declared cookie, shown in the cookie policy table.",
            factory.Property(BuiltInDataTypes.Textstring, "cookieName", "Name", "Literal name or pattern, e.g. _ga_*", 0),
            factory.Property(BuiltInDataTypes.Textstring, "provider", "Provider", "NDSTK, Google, YouTube…", 1),
            factory.Property(DataTypes.CookieCategory, "category", "Category", sortOrder: 2),
            factory.Property(BuiltInDataTypes.Textarea, "purpose", "Purpose", sortOrder: 3),
            factory.Property(BuiltInDataTypes.Textstring, "duration", "Duration", "\"12 månader\", \"Session\"", 4),
            factory.Property(DataTypes.StorageType, "storageType", "Storage type", sortOrder: 5));
    }

    private Task<IContentType> EnsureElementTypeAsync(
        Guid key,
        string alias,
        string name,
        string icon,
        string description,
        params IPropertyType[] properties)
        => factory.EnsureContentTypeAsync(key, alias, name, icon, type =>
        {
            type.IsElement = true;
            type.Description = description;
            NdstkContentTypeFactory.AddGroup(type, DeriveKey(key, 1), "content", "Content", 0, properties);
        });

    // --------------------------------------------------------------- data types

    private async Task InstallDataTypesAsync()
    {
        await factory.EnsureDataTypeAsync(
            DataTypes.StartContentBlocks,
            "NDSTK - Page content blocks",
            Constants.PropertyEditors.Aliases.BlockList,
            "Umb.PropertyEditorUi.BlockList",
            new Dictionary<string, object>
            {
                ["blocks"] = new object[]
                {
                    Block(ElementTypes.Hero, "Hero"),
                    Block(ElementTypes.NewsList, "News list"),
                    Block(ElementTypes.Post, "News post"),
                    Block(ElementTypes.Text, "Rich text"),
                },
            });

        await factory.EnsureDataTypeAsync(
            DataTypes.SidebarWidgetBlocks,
            "NDSTK - Sidebar widgets",
            Constants.PropertyEditors.Aliases.BlockList,
            "Umb.PropertyEditorUi.BlockList",
            new Dictionary<string, object>
            {
                ["blocks"] = new object[]
                {
                    Block(ElementTypes.CtaWidget, "Call to action"),
                    Block(ElementTypes.ContactWidget, "Contact"),
                    Block(ElementTypes.TagsWidget, "Tags"),
                    Block(ElementTypes.Text, "Rich text"),
                },
            });

        await factory.EnsureDataTypeAsync(
            DataTypes.MenuPicker,
            "NDSTK - Header menu picker",
            Constants.PropertyEditors.Aliases.MultiNodeTreePicker,
            "Umb.PropertyEditorUi.ContentPicker",
            new Dictionary<string, object>
            {
                ["minNumber"] = 0,
                ["maxNumber"] = 0,
            });

        // The old site used a bespoke "key/value dropdown" property editor UI for this. That UI is
        // not part of this solution, so a plain flexible dropdown carries the same four values.
        await factory.EnsureDataTypeAsync(
            DataTypes.MetaRobots,
            "NDSTK - Meta robots",
            Constants.PropertyEditors.Aliases.DropDownListFlexible,
            "Umb.PropertyEditorUi.Dropdown",
            new Dictionary<string, object>
            {
                ["multiple"] = false,
                ["items"] = new[] { "INDEX,FOLLOW", "INDEX,NOFOLLOW", "NOINDEX,FOLLOW", "NOINDEX,NOFOLLOW" },
            });

        // Unlike the cookie category / storage type dropdowns above, this Block List belongs here
        // rather than in InstallCookieDataTypesAsync: it references the cookieDefinition element
        // type by key, so it must be created after InstallElementTypesAsync has run.
        await factory.EnsureDataTypeAsync(
            DataTypes.CookieRegistry,
            "NDSTK - Cookie registry",
            Constants.PropertyEditors.Aliases.BlockList,
            "Umb.PropertyEditorUi.BlockList",
            new Dictionary<string, object>
            {
                ["blocks"] = new object[] { Block(ElementTypes.CookieDefinition, "Cookie") },
            });
    }

    private static Dictionary<string, object> Block(Guid elementTypeKey, string label) => new()
    {
        ["contentElementTypeKey"] = elementTypeKey,
        ["label"] = label,
        ["editorSize"] = "medium",
    };

    // ----------------------------------------------------------- document types

    private async Task InstallDocumentTypesAsync(Dictionary<Guid, ITemplate> templates)
    {
        await factory.PreloadDataTypesAsync(
            DataTypes.StartContentBlocks,
            DataTypes.SidebarWidgetBlocks,
            DataTypes.MenuPicker,
            DataTypes.MetaRobots,
            DataTypes.CookieRegistry);

        IContentType baseType = await factory.EnsureContentTypeAsync(
            DocumentTypes.Base, "base", "Base", "icon-brick", type =>
            {
                type.Description = "Composition with the SEO fields every page shares.";
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Base, 1), "seo", "SEO", 100,
                    factory.Property(DataTypes.MetaRobots, "metaRobots", "Meta robots", sortOrder: 0),
                    factory.Property(BuiltInDataTypes.Textstring, "metaTitle", "Meta title", "Falls back to the node name.", 1),
                    factory.Property(BuiltInDataTypes.Textarea, "metaDescription", "Meta description", sortOrder: 2),
                    factory.Property(BuiltInDataTypes.Tags, "metaKeywords", "Meta keywords", sortOrder: 3),
                    factory.Property(BuiltInDataTypes.Textstring, "metaAuthor", "Meta author", sortOrder: 4));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Start, "start", "Start", "icon-home", type =>
            {
                type.AllowedAsRoot = true;
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.Start]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Start, 1), "content", "Content", 0,
                    factory.Property(DataTypes.StartContentBlocks, "contentBlocks", "Content", "The blocks shown in the left column.", 0));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Settings, "settings", "Settings", "icon-settings", type =>
            {
                type.Description = "Site-wide configuration: header menu, sidebar and footer.";
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Settings, 1), "settings", "Settings", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "siteName", "Site name", sortOrder: 0),
                    factory.Property(DataTypes.MenuPicker, "menu", "Header menu", sortOrder: 1),
                    factory.Property(BuiltInDataTypes.ContentPicker, "loginPage", "Login page", "Target of the Logga in button in the sidebar.", 2),
                    factory.Property(DataTypes.SidebarWidgetBlocks, "sidebarWidgets", "Sidebar widgets", sortOrder: 3),
                    factory.Property(BuiltInDataTypes.Textstring, "footerText", "Footer text", sortOrder: 4),
                    factory.Property(BuiltInDataTypes.ContentPicker, "cookiePolicyPage", "Cookie policy page",
                        "Linked from the consent banner and the footer.", 5));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Articles, "articles", "Articles", "icon-folders", type =>
            {
                type.Description = "Folder holding the news articles.";
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Article, "article", "Article", "icon-newspaper", type =>
            {
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.Article]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Article, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0),
                    factory.Property(BuiltInDataTypes.DatePicker, "publishDate", "Date", sortOrder: 1),
                    factory.Property(BuiltInDataTypes.Textarea, "summary", "Summary", "Shown in the news list on the start page.", 2),
                    factory.Property(BuiltInDataTypes.ImageMediaPicker, "image", "Image", sortOrder: 3),
                    factory.Property(BuiltInDataTypes.RichtextEditor, "body", "Body", sortOrder: 4),
                    factory.Property(BuiltInDataTypes.Tags, "tags", "Tags", sortOrder: 5));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Error, "error", "Error", "icon-application-error", type =>
            {
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.Error]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Error, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
                    factory.Property(BuiltInDataTypes.Textarea, "text", "Text", sortOrder: 1));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Login, "login", "Login", "icon-combination-lock", type =>
            {
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.Login]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Login, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", sortOrder: 0),
                    factory.Property(BuiltInDataTypes.Textarea, "description", "Description", sortOrder: 1),
                    factory.Property(BuiltInDataTypes.Textstring, "subText", "Sub text", sortOrder: 2));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.CookiePolicy, "cookiePolicy", "Cookie policy", "icon-lock", type =>
            {
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.CookiePolicy]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.CookiePolicy, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0),
                    factory.Property(BuiltInDataTypes.RichtextEditor, "introduction", "Introduction", sortOrder: 1),
                    factory.Property(DataTypes.CookieRegistry, "cookies", "Declared cookies", sortOrder: 2),
                    factory.Property(BuiltInDataTypes.RichtextEditor, "outro", "Closing text", sortOrder: 3));
            });

        // Second pass: every type exists now, so the structure can reference it.
        await factory.SetAllowedChildrenAsync(
            DocumentTypes.Start,
            (DocumentTypes.Settings, "settings"),
            (DocumentTypes.Articles, "articles"),
            (DocumentTypes.Login, "login"),
            (DocumentTypes.Error, "error"),
            (DocumentTypes.CookiePolicy, "cookiePolicy"));

        await factory.SetAllowedChildrenAsync(
            DocumentTypes.Articles,
            (DocumentTypes.Article, "article"),
            (DocumentTypes.Articles, "articles"));
    }

    /// <summary>
    /// Property groups need their own stable keys. Deriving them from the owning type's key keeps
    /// the key registry small while staying deterministic across installs.
    /// </summary>
    private static Guid DeriveKey(Guid owner, byte discriminator)
    {
        Span<byte> bytes = stackalloc byte[16];
        owner.TryWriteBytes(bytes);
        bytes[15] = (byte)(bytes[15] ^ 0x80 ^ discriminator);
        return new Guid(bytes);
    }
}
