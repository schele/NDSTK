using System.Text.Json.Nodes;

using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner.Desktop;

namespace NDSTK.Desktop.Tests;

/// <summary>
/// What the settings file promises: profiles survive a round trip, no credential is legible in the
/// file, an unreadable one costs its field and nothing else, and a file written by the flat
/// pre-profiles build still opens.
/// </summary>
/// <remarks>
/// Every test writes into a folder of its own rather than into
/// <c>%LOCALAPPDATA%\NDSTK.CookieScanner</c>, which is the real window's file: a suite that ran
/// against it would delete the operator's saved sites, and two tests running in parallel would
/// overwrite each other. That is the whole reason <see cref="DashboardSettings.Load(string)"/> and
/// <see cref="DashboardSettings.Save(string)"/> take a path at all - the parameterless pair the app
/// uses is a one-line call onto these.
/// <para>
/// The ciphertext these tests read back is real DPAPI, protected under whichever user runs them, so
/// they pass on a developer machine and in CI without either sharing a key. That is also why test 4
/// corrupts a blob rather than pasting one from elsewhere: a blob captured from another machine
/// would be exactly the same assertion with a fixture that cannot be regenerated.
/// </para>
/// </remarks>
public class DashboardSettingsTests
{
    /// <summary>A settings.json in a folder of its own, removed when the test ends.</summary>
    private sealed class TempSettings : IDisposable
    {
        // Fully qualified, because the Path property below shadows the System.IO.Path this needs.
        private readonly string folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "NDSTK.Desktop.Tests", Guid.NewGuid().ToString("n"));

        public TempSettings() => Directory.CreateDirectory(folder);

        public string Path => System.IO.Path.Combine(folder, "settings.json");

        public string Text => File.ReadAllText(Path);

        public void Write(string json) => File.WriteAllText(Path, json);

        public void Dispose()
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A leftover temp folder is not a failing test. Swallowed rather than reported,
                // because a Dispose that threw would replace the real assertion failure with this.
            }
        }
    }

    private static SiteProfile Profile(string url) => new(
        Url: url,
        MaxPages: 7,
        Locale: Locale.En,
        DryRun: false,
        MemberEmail: "member@ndstk.se",
        MemberPassword: "hunter2-but-longer",
        ClientId: "cookie-scanner-ndstk");

    /// <summary>
    /// The exact file the pre-profiles build wrote, down to the integer locale.
    /// </summary>
    /// <remarks>
    /// "Locale": 0 rather than "Sv" because that is what a file written before the shared
    /// JsonStringEnumConverter reached this class holds, and the converter still reads numbers back.
    /// A migration tested only against the newer spelling would leave the oldest files - the ones
    /// most likely to exist - loading as Swedish by accident rather than by conversion.
    /// </remarks>
    [Fact]
    public void A_flat_pre_profiles_file_becomes_one_profile()
    {
        using TempSettings file = new();

        file.Write(
            """
            {
              "Url": "https://localhost:44351",
              "MaxPages": 12,
              "Locale": 0,
              "MemberEmail": "old@ndstk.se",
              "ClientId": "old-client",
              "DryRun": false
            }
            """);

        DashboardSettings settings = DashboardSettings.Load(file.Path);

        SiteProfile profile = Assert.Single(settings.Sites);

        Assert.Equal("https://localhost:44351", profile.Url);
        Assert.Equal(12, profile.MaxPages);
        Assert.Equal(Locale.Sv, profile.Locale);
        Assert.Equal("old@ndstk.se", profile.MemberEmail);
        Assert.Equal("old-client", profile.ClientId);
        Assert.False(profile.DryRun);

        // The old file never held one, so there is nothing to migrate into it and nothing to warn
        // about: an empty password is the truth about what that file contained.
        Assert.Equal("", profile.MemberPassword);
        Assert.Empty(settings.Warnings);

        // The one profile is the selected one. A migrated file whose only site was not selected
        // would open the window on "New site" with the remembered URL a dropdown away.
        Assert.Equal("https://localhost:44351", settings.SelectedUrl);
    }

    [Fact]
    public void Two_profiles_round_trip_through_save_and_load()
    {
        using TempSettings file = new();

        DashboardSettings saved = new(
            [Profile("https://localhost:44351"), Profile("https://ndstk.se") with { MaxPages = 40 }],
            SelectedUrl: "https://ndstk.se");

        saved.Save(file.Path);

        DashboardSettings loaded = DashboardSettings.Load(file.Path);

        Assert.Empty(loaded.Warnings);
        Assert.Equal("https://ndstk.se", loaded.SelectedUrl);

        // Record equality, member by member, so a field added to SiteProfile later is covered by
        // this test without anything here having to be told about it.
        Assert.Equal(saved.Sites, loaded.Sites);
    }

    /// <summary>
    /// The point of the encryption: the file is not a place to read a password out of.
    /// </summary>
    /// <remarks>
    /// Asserted against the raw text rather than against the parsed document, because what matters
    /// is what someone opening the file in an editor can see - and a value could survive
    /// deserialisation into a field this test does not know about.
    /// </remarks>
    [Fact]
    public void The_saved_file_holds_no_plaintext_credential()
    {
        using TempSettings file = new();

        SiteProfile profile = Profile("https://localhost:44351");

        new DashboardSettings([profile], profile.Url).Save(file.Path);

        string text = file.Text;

        Assert.DoesNotContain(profile.MemberEmail, text, StringComparison.Ordinal);
        Assert.DoesNotContain(profile.MemberPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(profile.ClientId, text, StringComparison.Ordinal);

        // The URL is not a credential and stays legible: it is what a human needs to recognise the
        // profile whose blobs these are, and hiding it would make the file unreadable for nothing.
        Assert.Contains(profile.Url, text, StringComparison.Ordinal);

        JsonNode stored = JsonNode.Parse(text)!["Sites"]![0]!;

        foreach (string field in new[] { "MemberEmail", "MemberPassword", "ClientId" })
        {
            Assert.StartsWith(ProtectedText.Prefix, stored[field]!.GetValue<string>(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A blob that will not open costs its own field and says so, and costs nothing else.
    /// </summary>
    /// <remarks>
    /// The realistic causes are a settings file copied from another machine or another Windows
    /// account, both of which produce exactly this: base64 that decodes and a DPAPI call that
    /// refuses it. Dropping the profile would lose a URL and a page count that are still perfectly
    /// good; throwing would cost the window every profile over one field.
    /// </remarks>
    [Fact]
    public void A_corrupted_blob_clears_its_field_and_warns()
    {
        using TempSettings file = new();

        SiteProfile intact = Profile("https://ndstk.se");
        SiteProfile damaged = Profile("https://localhost:44351");

        new DashboardSettings([damaged, intact], damaged.Url).Save(file.Path);

        JsonNode document = JsonNode.Parse(file.Text)!;

        // The prefix is kept and only the payload is replaced, so this is the "a blob that does not
        // open" case rather than the "not a blob at all" one. AAAA is valid base64 and is not a
        // DPAPI blob.
        document["Sites"]![0]!["MemberPassword"] = ProtectedText.Prefix + "AAAA";

        file.Write(document.ToJsonString());

        DashboardSettings loaded = DashboardSettings.Load(file.Path);

        Assert.Equal(2, loaded.Sites.Count);
        Assert.Equal("", loaded.Sites[0].MemberPassword);

        // Everything else about that profile survived, including the two blobs beside the broken one.
        Assert.Equal(damaged with { MemberPassword = "" }, loaded.Sites[0]);
        Assert.Equal(intact, loaded.Sites[1]);

        string warning = Assert.Single(loaded.Warnings);

        Assert.Contains(damaged.Url, warning, StringComparison.Ordinal);
        Assert.Contains("password", warning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The URL is the profile's identity, so saving the same site twice edits it rather than
    /// growing the list.
    /// </summary>
    /// <remarks>
    /// Case-insensitive and trimmed because both are how one site spells itself differently: a URL
    /// pasted with a trailing space, or typed with a capital in the host. Neither is a second site,
    /// and a dropdown holding two entries that read identically is one nobody can use.
    /// </remarks>
    [Fact]
    public void Saving_a_site_again_replaces_it_rather_than_duplicating_it()
    {
        DashboardSettings settings = new([Profile("https://localhost:44351")], "https://localhost:44351");

        settings.Upsert(Profile("  HTTPS://LocalHost:44351  ") with { MaxPages = 99 });

        SiteProfile profile = Assert.Single(settings.Sites);

        Assert.Equal(99, profile.MaxPages);

        // Stored trimmed, so the value the dropdown shows and the value the next lookup compares
        // are the same string.
        Assert.Equal("HTTPS://LocalHost:44351", profile.Url);

        // A different site is a different entry, which is the other half of the same rule.
        settings.Upsert(Profile("https://ndstk.se"));

        Assert.Equal(2, settings.Sites.Count);
    }
}
