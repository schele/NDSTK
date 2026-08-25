namespace NDSTK.ContentModel;

/// <summary>
/// Stable keys for everything the installer creates. Keeping them in one place means the
/// installer is idempotent across environments: a re-run finds the existing entity by key
/// instead of creating a duplicate, and a uSync export produces the same GUIDs everywhere.
/// The document type / template keys are carried over from the previous NDSTK site so the
/// two content models line up if anything ever needs to be compared or migrated.
/// </summary>
internal static class NdstkKeys
{
    internal static class Templates
    {
        internal static readonly Guid Root = new("85504a4c-d7d4-4dc2-89ad-d5b3de6016bc");
        internal static readonly Guid Start = new("3f1e7824-78cc-484c-864d-90e25f12c90c");
        internal static readonly Guid Article = new("873307ad-a321-492a-9cac-17cfa0a388f8");
        internal static readonly Guid Error = new("6e7f09a6-b03e-4f67-b67d-afb1bef71f87");
        internal static readonly Guid Login = new("90fb6c48-3265-44be-a980-853595d75eb7");
        internal static readonly Guid MemberRegister = new("b1e10002-0000-4000-8000-000000000001");
        internal static readonly Guid MemberVerify = new("b1e10002-0000-4000-8000-000000000002");
        internal static readonly Guid MemberPortal = new("b1e10002-0000-4000-8000-000000000003");
        internal static readonly Guid SwishPayment = new("b1e10002-0000-4000-8000-000000000004");
    }

    internal static class DocumentTypes
    {
        internal static readonly Guid Base = new("a3b0e1d8-8b7b-48d0-b84b-3b913ea67146");
        internal static readonly Guid Start = new("cc364eaf-944c-4964-8a06-61927c5b3a29");
        internal static readonly Guid Settings = new("1c121fe7-5b22-4651-ade4-1e2b4ca9c514");
        internal static readonly Guid Articles = new("ed3309df-a6bb-4d99-ba13-d352f8e7d2d1");
        internal static readonly Guid Article = new("3056ddc9-9d8c-4a48-a8cf-64105023fc76");
        internal static readonly Guid Error = new("783208f3-b0bd-4b0f-9465-64c2d9ee1c7b");
        internal static readonly Guid Login = new("a97dd1d3-c8a1-4a8f-94f2-16c02d5f5909");
        internal static readonly Guid MemberRegister = new("b1e10001-0000-4000-8000-000000000001");
        internal static readonly Guid MemberVerify = new("b1e10001-0000-4000-8000-000000000002");
        internal static readonly Guid MemberPortal = new("b1e10001-0000-4000-8000-000000000003");
        internal static readonly Guid TrainingClasses = new("b1e10001-0000-4000-8000-000000000004");
        internal static readonly Guid TrainingClass = new("b1e10001-0000-4000-8000-000000000005");
        internal static readonly Guid SwishPayment = new("b1e10001-0000-4000-8000-000000000006");
    }

    /// <summary>
    /// The member type. This key is the one already in uSync/v18/MemberTypes/member.config, so the
    /// installer upgrades that member type rather than creating a second one beside it.
    /// </summary>
    internal static class MemberTypes
    {
        internal static readonly Guid Member = new("d59be02f-1df9-4228-aa1e-01917d806cda");
        internal const string MemberAlias = "Member";
    }

    /// <summary>Element types used as Block List blocks.</summary>
    internal static class ElementTypes
    {
        internal static readonly Guid Hero = new("e1e50001-0000-4000-8000-000000000001");
        internal static readonly Guid Post = new("e1e50001-0000-4000-8000-000000000002");
        internal static readonly Guid NewsList = new("e1e50001-0000-4000-8000-000000000003");
        internal static readonly Guid Text = new("e1e50001-0000-4000-8000-000000000004");
        internal static readonly Guid CtaWidget = new("e1e50001-0000-4000-8000-000000000005");
        internal static readonly Guid ContactWidget = new("e1e50001-0000-4000-8000-000000000006");
        internal static readonly Guid TagsWidget = new("e1e50001-0000-4000-8000-000000000007");
    }

    /// <summary>Data types this site adds on top of the Umbraco defaults.</summary>
    internal static class DataTypes
    {
        internal static readonly Guid StartContentBlocks = new("da7a0001-0000-4000-8000-000000000001");
        internal static readonly Guid SidebarWidgetBlocks = new("da7a0001-0000-4000-8000-000000000002");
        internal static readonly Guid MenuPicker = new("da7a0001-0000-4000-8000-000000000003");
        internal static readonly Guid MetaRobots = new("da7a0001-0000-4000-8000-000000000004");

        /// <summary>
        /// Date and time to the minute. Umbraco's built-in "Date Picker with time" shows seconds,
        /// which a class start time never needs, and its configuration is not ours to change - it is
        /// a type Umbraco ships and an editor may use for something else.
        /// </summary>
        internal static readonly Guid DateTimeNoSeconds = new("da7a0001-0000-4000-8000-000000000005");
    }

    /// <summary>Umbraco's built-in data types, reused as-is.</summary>
    internal static class BuiltInDataTypes
    {
        internal static readonly Guid Textstring = new("0cc0eba1-9960-42c9-bf9b-60e150b429ae");
        internal static readonly Guid Textarea = new("c6bac0dd-4ab9-45b1-8e30-e4b619ee5da3");
        internal static readonly Guid RichtextEditor = new("ca90c950-0aff-4e72-b976-a30b1ac57dad");
        internal static readonly Guid Tags = new("b6b73142-b9c1-4bf8-a16d-e1c23320b549");
        internal static readonly Guid MultiUrlPicker = new("b4e3535a-1753-47e2-8568-602cf8cfee6f");
        internal static readonly Guid DatePicker = new("5046194e-4237-453c-a547-15db3a07c4e1");
        internal static readonly Guid TrueFalse = new("92897bc6-a5f3-4ffe-ae27-f2e7e33dda49");
        internal static readonly Guid ImageMediaPicker = new("ad9f0cf2-bda2-45d5-9ea1-a63cfc873fd3");
        internal static readonly Guid ContentPicker = new("fd1e0da5-5606-4862-b679-5d0cf3a52a59");
        internal static readonly Guid Numeric = new("2e6d3631-066e-44b8-aec4-96f09099b2b5");

        /// <summary>Date and time, unlike DatePicker above which is date only.</summary>
        internal static readonly Guid DatePickerWithTime = new("e4d66c0f-b935-4200-81f0-025f7256b89a");
    }

    /// <summary>Nodes created by the content seeder when the site is still empty.</summary>
    internal static class Nodes
    {
        internal static readonly Guid Start = new("c0117e17-0000-4000-8000-000000000001");
        internal static readonly Guid Settings = new("c0117e17-0000-4000-8000-000000000002");
        internal static readonly Guid Articles = new("c0117e17-0000-4000-8000-000000000003");
        internal static readonly Guid Login = new("c0117e17-0000-4000-8000-000000000004");
        internal static readonly Guid Error = new("c0117e17-0000-4000-8000-000000000005");
        internal static readonly Guid MemberRegister = new("c0117e17-0000-4000-8000-000000000006");
        internal static readonly Guid MemberVerify = new("c0117e17-0000-4000-8000-000000000007");
        internal static readonly Guid MemberPortal = new("c0117e17-0000-4000-8000-000000000008");
        internal static readonly Guid TrainingClasses = new("c0117e17-0000-4000-8000-000000000009");
        internal static readonly Guid ExampleClass1 = new("c0117e17-0000-4000-8000-00000000000a");
        internal static readonly Guid ExampleClass2 = new("c0117e17-0000-4000-8000-00000000000b");
        internal static readonly Guid ExampleClass3 = new("c0117e17-0000-4000-8000-00000000000c");
        internal static readonly Guid SwishPayment = new("c0117e17-0000-4000-8000-00000000000d");
    }
}
