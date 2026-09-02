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
    IHostEnvironment hostEnvironment,
    ILogger<NdstkContentModelInstaller> logger)
{
    // Declared once because it is used twice: in the fresh-install declaration and in the upgrade
    // that reaches a site already installed. Two copies of the same help text drift apart.
    private const string VenueAddressHelp =
        "Klubbens adress, t.ex. \"Gymnastik- och idrottshögskolan, Lidingövägen 1, Stockholm\". " +
        "Platsen på varje träning blir en länk till Google Maps på den här adressen. Lämnas den " +
        "tom visas platsen som vanlig text.";

    /// <summary>Same reason as above: declared once, used by both the fresh install and the upgrade.</summary>
    private const string SidebarWidgetsHelp = "The boxes shown in the right column, on every page.";

    /// <summary>
    /// The blocks the sidebar accepts, in the order the "add content" picker offers them. Declared
    /// once because it is used twice: in the data type a fresh install creates, and in the upgrade
    /// that reaches a site which already has that data type.
    /// </summary>
    private static readonly Dictionary<string, object>[] SidebarBlocks =
    [
        Block(ElementTypes.MemberWidget, "Medlem"),
        Block(ElementTypes.CtaWidget, "Call to action"),
        Block(ElementTypes.ContactWidget, "Contact"),
        Block(ElementTypes.TagsWidget, "Tags"),
        Block(ElementTypes.Text, "Rich text"),
    ];

    public async Task InstallAsync()
    {
        await languages.InstallAsync();

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
            BuiltInDataTypes.Numeric,
            BuiltInDataTypes.DatePickerWithTime);

        await InstallElementTypesAsync();
        await InstallDataTypesAsync();
        await InstallDocumentTypesAsync(templates);
        await UpgradeExistingTypesAsync();

        logger.LogInformation("NDSTK content model is up to date.");
    }

    // ----------------------------------------------------------------- upgrades

    /// <summary>
    /// Adds fields to types that already exist. Everything above is create-if-missing, so a field
    /// added to an already-installed site has to come through the factory's Ensure*Async upgrade
    /// methods instead - declaring it in the block above would silently do nothing.
    /// </summary>
    private async Task UpgradeExistingTypesAsync()
    {
        // Prices are entered in kronor because that is what an editor thinks in. The conversion to
        // the öre stored in the payment tables happens once, in MembershipSettingsService.
        var settingsChanged = await factory.EnsureGroupAsync(
            DocumentTypes.Settings,
            DeriveKey(DocumentTypes.Settings, 2),
            "membership",
            "Medlemskap",
            factory.Property(BuiltInDataTypes.Numeric, "membershipFee", "Årsavgift (kr)", "Standard: 150.", 0),
            factory.Property(BuiltInDataTypes.Numeric, "familyFee", "Familjetillägg (kr)", "Tillägg per år för familjekonto, som låter kontot ha flera barn. Standard: 100.", 1),
            factory.Property(BuiltInDataTypes.Numeric, "firstClassPrice", "Pris första klassen (kr)", "Välkomstpris, en gång per barn. Standard: 100.", 2),
            factory.Property(BuiltInDataTypes.Numeric, "classPrice", "Pris per klass (kr)", "Standard: 200.", 3),
            factory.Property(BuiltInDataTypes.Numeric, "reminderHoursBefore", "Påminnelse (timmar innan)", "Standard: 24.", 4),
            factory.Property(BuiltInDataTypes.Numeric, "paymentHoldMinutes", "Betalningsreservation (minuter)", "Hur länge en obetald bokning håller sin plats, räknat från att Swish-betalningen startas. Måste vara längre än Swish egen tidsgräns på 5,5 minuter. Standard: 7.", 5),
            factory.Property(BuiltInDataTypes.Numeric, "cancellationDeadlineHours", "Avbokning stänger (timmar innan)", "Så nära starten går träningen inte längre att avboka. Standard: 12.", 6),
            factory.Property(BuiltInDataTypes.ContentPicker, "memberPortalPage", "Medlemssidan", "Dit medlemmen skickas efter inloggning.", 7),
            factory.Property(BuiltInDataTypes.ContentPicker, "registerPage", "Bli medlem-sidan", "Målet för Bli medlem-knapparna.", 8));

        if (settingsChanged)
        {
            // "Updated" rather than "added": this now also fires when only a label or a description
            // changed, and a log line that says "added" for a reworded help text sends the next
            // person looking for a field that was already there.
            logger.LogInformation("Updated the Medlemskap fields on the settings document type.");
        }

        // The club's address, so the court an editor types on a class can link to a map. It goes on
        // the settings group rather than on the class because "Bana 2" is a court and not a place -
        // one address, typed once, and every class listing gets a link without being touched.
        if (await factory.EnsureGroupAsync(
                DocumentTypes.Settings,
                DeriveKey(DocumentTypes.Settings, 1),
                "settings",
                "Settings",
                factory.Property(BuiltInDataTypes.Textstring, "venueAddress", "Adress", VenueAddressHelp, 5)))
        {
            logger.LogInformation("Added the club address to the settings document type.");
        }

        // The membership facts are administrative: a member may see them, but one who could edit
        // their own expiry would have a free membership, and one who could tick familjekonto would
        // have a free family account. The phone number is theirs to change.
        var memberChanged = await factory.EnsureMemberPropertiesAsync(
            MemberTypes.MemberAlias,
            "membership",
            "Membership",
            (factory.Property(BuiltInDataTypes.DatePicker, "membershipPaidUntil", "Membership paid until", "Inclusive last day of the paid membership.", 10), true, false),
            // Retired: the welcome price is now once per child and lives on ndstkParticipant.
            // Kept declared because the participant backfill reads it on an already-installed site.
            // Nothing writes it any more.
            (factory.Property(BuiltInDataTypes.TrueFalse, "firstClassDiscountUsed", "First class discount used (retired)", "No longer used. The welcome price is per child, on the participant.", 11), true, false),
            (factory.Property(BuiltInDataTypes.TrueFalse, "familjekonto", "Family account", "Set once the family supplement is paid. Lets the account hold more than one child.", 12), true, false),
            // The one the member may edit: it is their own contact detail, and nothing about it is
            // worth money. It appears on the class roster so a coach can reach a parent.
            (factory.Property(BuiltInDataTypes.Textstring, "telefon", "Phone", "The guardian's phone number.", 13), true, true));

        if (memberChanged)
        {
            logger.LogInformation("Updated the membership properties on the Member member type.");
        }

        // The class start time was created against Umbraco's "Date Picker with time", which shows
        // seconds. Changing the declaration above only reaches a fresh database, because
        // EnsureContentTypeAsync never revisits a type that exists - so the swap has to be asked for
        // explicitly here. Both data types store a Date, so every start time already entered stays
        // exactly as it is.
        if (await factory.RepointPropertyAsync(
                DocumentTypes.TrainingClass, "start", DataTypes.DateTimeNoSeconds))
        {
            logger.LogInformation("Class start times now use the minute-precision date picker.");
        }

        // The coach became a picker. Not a repoint of the old field: a content picker stores a UDI
        // where the text field stored a name, so the two cannot share a column - it is a new
        // property, and NdstkInstructorBackfill turns the old names into nodes and fills it in.
        //
        // The old text field is not declared here at all. It only ever existed on sites installed
        // before the picker, and the backfill deletes it once it has carried the names over - which
        // happens after this method, so a site arriving here with names still in it keeps them long
        // enough to migrate.
        var classChanged = await factory.EnsureGroupAsync(
            DocumentTypes.TrainingClass,
            DeriveKey(DocumentTypes.TrainingClass, 1),
            "content",
            "Träningen",
            factory.Property(BuiltInDataTypes.ContentPicker, "coach", "Tränare", "Välj en tränare under Tränare.", 5));

        if (classChanged)
        {
            logger.LogInformation("Updated the fields on the training class document type.");
        }

        // Before the group below, and before NdstkSidebarWidgetMove writes a Medlem block into any
        // sidebar: a Block List refuses a block whose element type its data type does not list.
        if (await factory.EnsureBlocksAsync(DataTypes.SidebarWidgetBlocks, SidebarBlocks))
        {
            logger.LogInformation("Added the missing blocks to the sidebar widget data type.");
        }

        // The sidebar moved off Settings and onto the start page. Only the field is added here;
        // carrying the widgets an editor has already arranged across, and then dropping the old
        // field, is content work and belongs in NdstkSidebarWidgetMove.
        if (await factory.EnsureGroupAsync(
                DocumentTypes.Start,
                DeriveKey(DocumentTypes.Start, 2),
                "sidebar",
                "Sidebar",
                factory.Property(DataTypes.SidebarWidgetBlocks, "sidebarWidgets", "Sidebar widgets", SidebarWidgetsHelp, 0)))
        {
            logger.LogInformation("Added the sidebar widgets to the start document type.");
        }

        // And a tab, which the call above cannot make it - see the remarks on EnsureTabAsync. Sort
        // order 1 puts it between Content at 0 and the SEO tab the base composition brings in at
        // 100, so the tabs read Content, Sidebar, SEO.
        if (await factory.EnsureTabAsync(DocumentTypes.Start, DeriveKey(DocumentTypes.Start, 2), 1))
        {
            logger.LogInformation("The sidebar widgets are a tab of their own on the start document type.");
        }
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
            (Templates.MemberRegister, "MemberRegister", "MemberRegister"),
            (Templates.MemberVerify, "MemberVerify", "MemberVerify"),
            (Templates.MemberPortal, "MemberPortal", "MemberPortal"),
            (Templates.SwishPayment, "SwishPayment", "SwishPayment"),
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

        // Only the wording is an editor's to choose. Where the two buttons go is not: the targets
        // are already picked once on Settings, as Medlemssidan and Login page, and a second pair of
        // pickers here is how the two drift apart - a login link in the sidebar pointing somewhere
        // other than the one login redirect the site actually uses.
        await EnsureElementTypeAsync(ElementTypes.MemberWidget, "memberWidgetBlock", "Widget: Medlem", "icon-user",
            "Sidebar box with the member's way in: Mina sidor when signed in, Logga in when not.",
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Rubrik", "Standard: Medlem.", 0),
            factory.Property(BuiltInDataTypes.Textstring, "memberLinkLabel", "Knapp för inloggad medlem", "Standard: Mina sidor.", 1),
            factory.Property(BuiltInDataTypes.Textstring, "guestLinkLabel", "Knapp för besökare", "Standard: Logga in.", 2));
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
                ["blocks"] = SidebarBlocks,
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

        // Same editor as Umbraco's "Date Picker with time", one field shorter. A class starts on the
        // hour or the half hour; seconds are three characters of noise in every date an editor types.
        //
        // A data type of our own rather than reconfiguring the built-in: that one ships with Umbraco
        // and an editor may reach for it elsewhere, so its format is not ours to change.
        await factory.EnsureDataTypeAsync(
            DataTypes.DateTimeNoSeconds,
            "NDSTK - Datum och tid",
            Constants.PropertyEditors.Aliases.DateTime,
            "Umb.PropertyEditorUi.DatePicker",
            new Dictionary<string, object>
            {
                ["format"] = "YYYY-MM-DD HH:mm",
            },
            ValueStorageType.Date);
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
            DataTypes.DateTimeNoSeconds);

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

                // A tab of its own rather than a second property under Content. The sidebar is
                // site-wide - it is read off this node for every page, not just the start page -
                // and a block list sitting directly beneath "the blocks shown in the left column"
                // reads as more of the start page, which is exactly the wrong thing to think while
                // editing it.
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Start, 2), "sidebar", "Sidebar", 1,
                    factory.Property(DataTypes.SidebarWidgetBlocks, "sidebarWidgets", "Sidebar widgets", SidebarWidgetsHelp, 0));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Settings, "settings", "Settings", "icon-settings", type =>
            {
                // The sidebar's own boxes are not here: they are content, and they live on the start
                // page beside the content of the column next to them. What is left of the sidebar
                // on this node is the two pickers the Medlem box points at.
                type.Description = "Site-wide configuration: header menu, footer and the member pages.";
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Settings, 1), "settings", "Settings", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "siteName", "Site name", sortOrder: 0),
                    factory.Property(DataTypes.MenuPicker, "menu", "Header menu", sortOrder: 1),
                    factory.Property(BuiltInDataTypes.ContentPicker, "loginPage", "Login page", "Target of the Logga in button in the sidebar.", 2),
                    factory.Property(BuiltInDataTypes.Textstring, "footerText", "Footer text", sortOrder: 4),
                    factory.Property(BuiltInDataTypes.Textstring, "venueAddress", "Adress", VenueAddressHelp, 5));
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
            DocumentTypes.MemberRegister, "memberRegister", "Bli medlem", "icon-user-add", type =>
            {
                type.Description = "Registreringsformuläret för nya medlemmar.";
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.MemberRegister]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.MemberRegister, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0),
                    factory.Property(BuiltInDataTypes.Textarea, "description", "Description", sortOrder: 1));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.MemberVerify, "memberVerify", "Verifiera e-post", "icon-message-open", type =>
            {
                type.Description = "Landningssidan för länken i bekräftelsemailet.";
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.MemberVerify]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.MemberVerify, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.MemberPortal, "memberPortal", "Mina sidor", "icon-user", type =>
            {
                type.Description = "Medlemssidan: bokningar, påminnelser och bokningsbara klasser.";
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.MemberPortal]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.MemberPortal, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0),
                    factory.Property(BuiltInDataTypes.Textarea, "description", "Description", sortOrder: 1));
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.TrainingClasses, "trainingClasses", "Träningar", "icon-calendar-alt", type =>
            {
                type.Description = "Mappen som håller träningsklasserna.";
            });

        // No template: a class is data the portal renders, not a page of its own.
        await factory.EnsureContentTypeAsync(
            DocumentTypes.TrainingClass, "trainingClass", "Träningsklass", "icon-tennis-ball", type =>
            {
                type.Description = "En enskild träning med ett maxantal deltagare.";
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.TrainingClass, 1), "content", "Träningen", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "title", "Namn", "Faller tillbaka på nodens namn.", 0),
                    factory.Property(BuiltInDataTypes.Textarea, "description", "Beskrivning", sortOrder: 1),
                    // Swedish local time, converted to UTC on the way into the booking tables.
                    factory.Property(DataTypes.DateTimeNoSeconds, "start", "Starttid", "Datum och klockslag, svensk tid.", 2),
                    factory.Property(BuiltInDataTypes.Numeric, "durationMinutes", "Längd (minuter)", "Standard: 60.", 3),
                    factory.Property(BuiltInDataTypes.Numeric, "capacity", "Max antal deltagare", "Hur många som kan boka den här träningen.", 4),
                    factory.Property(BuiltInDataTypes.ContentPicker, "coach", "Tränare", "Välj en tränare under Tränare.", 5),
                    factory.Property(BuiltInDataTypes.Textstring, "location", "Plats", sortOrder: 6));
            });

        // Folder and profile for the coaches. No templates: an instructor is data the class listing
        // renders in a dialog, not a page of its own - the same reasoning as a training class.
        await factory.EnsureContentTypeAsync(
            DocumentTypes.Instructors, "instructors", "Tränare", "icon-users", type =>
            {
                type.Description = "Mappen som håller tränarna.";
            });

        await factory.EnsureContentTypeAsync(
            DocumentTypes.Instructor, "instructor", "Tränare", "icon-user", type =>
            {
                type.Description = "En tränare. Namnet är nodens namn.";
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.Instructor, 1), "content", "Tränaren", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "role", "Roll", "Till exempel Huvudtränare. Frivilligt.", 0),
                    factory.Property(BuiltInDataTypes.ImageMediaPicker, "photo", "Foto", "Visas i rutan som öppnas när någon klickar på namnet.", 1),
                    factory.Property(BuiltInDataTypes.Textarea, "quote", "Citat", "Något tränaren själv säger. Frivilligt.", 2),
                    // Rich text, so a list of merits can actually be a list.
                    factory.Property(BuiltInDataTypes.RichtextEditor, "merits", "Meriter", "Erfarenhet, utbildning, tävlingar.", 3));
            });

        // A child of the portal, so it inherits the portal's public access: an anonymous visitor
        // cannot reach a payment page even with a reference in hand.
        await factory.EnsureContentTypeAsync(
            DocumentTypes.SwishPayment, "swishPayment", "Betalning (Swish)", "icon-coins", type =>
            {
                type.Description = "Den mockade Swish-betalningen. Nås med ?ref= i adressen.";
                type.AddContentType(baseType);
                NdstkContentTypeFactory.UseTemplate(type, templates[Templates.SwishPayment]);
                NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.SwishPayment, 1), "content", "Content", 0,
                    factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0));
            });

        // Second pass: every type exists now, so the structure can reference it.
        await factory.SetAllowedChildrenAsync(
            DocumentTypes.Start,
            (DocumentTypes.Settings, "settings"),
            (DocumentTypes.Articles, "articles"),
            (DocumentTypes.Login, "login"),
            (DocumentTypes.MemberRegister, "memberRegister"),
            (DocumentTypes.MemberVerify, "memberVerify"),
            (DocumentTypes.MemberPortal, "memberPortal"),
            (DocumentTypes.TrainingClasses, "trainingClasses"),
            (DocumentTypes.Instructors, "instructors"),
            (DocumentTypes.Error, "error"));

        await factory.SetAllowedChildrenAsync(
            DocumentTypes.TrainingClasses,
            (DocumentTypes.TrainingClass, "trainingClass"));

        await factory.SetAllowedChildrenAsync(
            DocumentTypes.Instructors,
            (DocumentTypes.Instructor, "instructor"));

        await factory.SetAllowedChildrenAsync(
            DocumentTypes.MemberPortal,
            (DocumentTypes.SwishPayment, "swishPayment"));

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
