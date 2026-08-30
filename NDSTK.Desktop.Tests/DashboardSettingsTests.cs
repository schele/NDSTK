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
        ClientId: "cookie-scanner-ndstk",
        ClientSecret: "client-secret-alpha-1");

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

        // The old file never held either, so there is nothing to migrate into them and nothing to
        // warn about: an empty password and an empty secret are the truth about what that file
        // contained. The secret was not merely absent from that shape - no build had ever stored one.
        Assert.Equal("", profile.MemberPassword);
        Assert.Equal("", profile.ClientSecret);
        Assert.Empty(settings.Warnings);

        // The one profile is the selected one. A migrated file whose only site was not selected
        // would open the window on "New site" with the remembered URL a dropdown away.
        Assert.Equal("https://localhost:44351", settings.SelectedUrl);
    }

    /// <summary>
    /// A flat file whose URL is blank migrates to nothing, not to a profile nobody can reach.
    /// </summary>
    /// <remarks>
    /// The old file allowed a blank URL - it stored whatever had last been typed - and a profile
    /// carrying one is worse than no profile at all. The URL is the identity, the dropdown label and
    /// the delete key, so a blank one renders as an option with the same empty value as "New site":
    /// picking it does nothing, Delete stays disabled because the select reads as unselected, and it
    /// survives every save. Nothing in it is worth that - it cannot even name a site to scan.
    /// </remarks>
    [Fact]
    public void A_flat_file_with_no_url_migrates_to_no_profiles()
    {
        using TempSettings file = new();

        file.Write(
            """
            {
              "Url": "",
              "MaxPages": 12,
              "Locale": 0,
              "MemberEmail": "old@ndstk.se",
              "ClientId": "old-client",
              "DryRun": false
            }
            """);

        DashboardSettings settings = DashboardSettings.Load(file.Path);

        Assert.Empty(settings.Sites);
        Assert.Null(settings.SelectedUrl);

        // Not a fault worth reporting either: an empty URL is a field nobody filled in, not a value
        // that was lost on the way out of the file.
        Assert.Empty(settings.Warnings);
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

        // The one this file did not hold at all until the secret moved into the profile. It is the
        // credential that can write to a live policy page, so "legible nowhere in the file" is the
        // promise that had to be re-made rather than assumed from the three above it.
        Assert.DoesNotContain(profile.ClientSecret, text, StringComparison.Ordinal);

        // The URL is not a credential and stays legible: it is what a human needs to recognise the
        // profile whose blobs these are, and hiding it would make the file unreadable for nothing.
        Assert.Contains(profile.Url, text, StringComparison.Ordinal);

        JsonNode stored = JsonNode.Parse(text)!["Sites"]![0]!;

        foreach (string field in new[] { "MemberEmail", "MemberPassword", "ClientId", "ClientSecret" })
        {
            Assert.StartsWith(ProtectedText.Prefix, stored[field]!.GetValue<string>(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A file the previous build wrote - new shape, but no <c>ClientSecret</c> key - opens with an
    /// empty secret rather than failing.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than produced by an older build, because there is no older build to
    /// run here: the shape below is exactly what one wrote, four keys per profile and a
    /// <c>Sites</c> array to send Load down the new-shape path. The behaviour it pins is
    /// System.Text.Json's - a constructor parameter the document omits is filled with the
    /// parameter's default, which only works while <see cref="SiteProfile"/> keeps a default on it.
    /// Dropping that default would make every settings file on every machine load as no sites at
    /// all, which is the kind of thing that is only noticed once it has happened.
    /// <para>
    /// The three credentials are real blobs, protected here rather than pasted, so this is a file
    /// missing one key rather than a file that also fails to decrypt - see the class remark.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_file_from_before_the_secret_loads_with_an_empty_secret()
    {
        using TempSettings file = new();

        string email = ProtectedText.Protect("old@ndstk.se");
        string password = ProtectedText.Protect("old-password");
        string clientId = ProtectedText.Protect("old-client");

        file.Write(
            $$"""
            {
              "Sites": [
                {
                  "Url": "https://localhost:44351",
                  "MaxPages": 12,
                  "Locale": "Sv",
                  "DryRun": false,
                  "MemberEmail": "{{email}}",
                  "MemberPassword": "{{password}}",
                  "ClientId": "{{clientId}}"
                }
              ],
              "SelectedUrl": "https://localhost:44351"
            }
            """);

        DashboardSettings settings = DashboardSettings.Load(file.Path);

        SiteProfile profile = Assert.Single(settings.Sites);

        Assert.Equal("", profile.ClientSecret);

        // Everything the older build did write is still exactly what it wrote: a missing key is a
        // field nobody filled in, not a file that failed to read.
        Assert.Equal("https://localhost:44351", profile.Url);
        Assert.Equal(12, profile.MaxPages);
        Assert.Equal("old@ndstk.se", profile.MemberEmail);
        Assert.Equal("old-password", profile.MemberPassword);
        Assert.Equal("old-client", profile.ClientId);

        // And nothing is warned about. An absent key is not a credential that was lost - warning
        // here would tell every operator upgrading to this build to retype something they never had.
        Assert.Empty(settings.Warnings);
    }

    /// <summary>
    /// The secret's blob follows the same rule as the three beside it: unreadable costs that field
    /// and says which one.
    /// </summary>
    /// <remarks>
    /// Its own test rather than a second case on
    /// <see cref="A_corrupted_blob_clears_its_field_and_warns"/>, because the wording is what is
    /// being checked as much as the clearing: the warning has to name the client secret specifically,
    /// or an operator with four credentials in one profile is told to retype an unnamed one.
    /// </remarks>
    [Fact]
    public void A_corrupted_client_secret_clears_that_field_and_names_it()
    {
        using TempSettings file = new();

        SiteProfile damaged = Profile("https://localhost:44351");

        new DashboardSettings([damaged], damaged.Url).Save(file.Path);

        JsonNode document = JsonNode.Parse(file.Text)!;

        document["Sites"]![0]!["ClientSecret"] = ProtectedText.Prefix + "AAAA";

        file.Write(document.ToJsonString());

        DashboardSettings loaded = DashboardSettings.Load(file.Path);

        // The rest of the profile - the other three credentials included - is untouched.
        Assert.Equal(damaged with { ClientSecret = "" }, Assert.Single(loaded.Sites));

        string warning = Assert.Single(loaded.Warnings);

        Assert.Contains(damaged.Url, warning, StringComparison.Ordinal);
        Assert.Contains("client secret", warning, StringComparison.OrdinalIgnoreCase);
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
    /// A stored value with no ciphertext prefix is refused, not used.
    /// </summary>
    /// <remarks>
    /// The case <see cref="ProtectedText.TryUnprotect"/>'s prefix check exists for. Everything in a
    /// new-shape file was written by <see cref="ProtectedText.Protect"/>, so a bare string in one was
    /// put there by hand or by something that is not this program - and signing in with an
    /// unexplained credential is not a thing to do quietly. It is cleared and warned about, exactly
    /// as a blob that will not decrypt is, and the operator is told which field to retype.
    /// <para>
    /// This is NOT the migration path. A settings file that genuinely predates profiles has no
    /// <c>sites</c> array at all, so it is read by the flat reader that expects plain text - see the
    /// first test in this file.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_stored_value_without_the_prefix_is_refused_and_warned_about()
    {
        using TempSettings file = new();

        SiteProfile profile = Profile("https://localhost:44351");

        new DashboardSettings([profile], profile.Url).Save(file.Path);

        JsonNode document = JsonNode.Parse(file.Text)!;

        document["Sites"]![0]!["MemberEmail"] = "plain@ndstk.se";

        file.Write(document.ToJsonString());

        DashboardSettings loaded = DashboardSettings.Load(file.Path);

        SiteProfile opened = Assert.Single(loaded.Sites);

        Assert.Equal("", opened.MemberEmail);

        // The two fields either side of it are untouched, so this really is per-field.
        Assert.Equal(profile.MemberPassword, opened.MemberPassword);
        Assert.Equal(profile.ClientId, opened.ClientId);

        string warning = Assert.Single(loaded.Warnings);

        Assert.Contains(profile.Url, warning, StringComparison.Ordinal);
        Assert.Contains("email", warning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deleting the selected profile takes it and the selection, and leaves everything else.
    /// </summary>
    /// <remarks>
    /// The selection goes to null rather than to the neighbour: deleting a site is not a request to
    /// open a different one, and a form that refilled itself with another site's credentials after a
    /// Delete would be the worst possible answer to that click. The form sets that null itself, but
    /// the removal below is what makes it the right answer.
    /// </remarks>
    [Fact]
    public void Removing_a_site_takes_that_one_and_leaves_the_rest()
    {
        SiteProfile kept = Profile("https://ndstk.se");
        SiteProfile doomed = Profile("https://localhost:44351");

        DashboardSettings settings = new([doomed, kept], doomed.Url);

        // Trimmed and case-insensitive here too, because the URL that comes back from the page is
        // whatever the dropdown's option held.
        settings.Remove("  HTTPS://LocalHost:44351  ");
        settings.SelectedUrl = null;

        Assert.Equal(kept, Assert.Single(settings.Sites));
        Assert.Null(settings.SelectedUrl);

        // Removing something that is not there is a no-op, not an error: the page can ask twice.
        settings.Remove("https://nothing.here");

        Assert.Single(settings.Sites);
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

        settings.Upsert(Profile("  HTTPS://LocalHost:44351  ") with
        {
            MaxPages = 99,
            MemberPassword = "  padded-password  ",
            ClientSecret = "  padded-secret  ",
        });

        SiteProfile profile = Assert.Single(settings.Sites);

        Assert.Equal(99, profile.MaxPages);

        // Stored trimmed, so the value the dropdown shows and the value the next lookup compares
        // are the same string.
        Assert.Equal("HTTPS://LocalHost:44351", profile.Url);

        // And so are the credentials, here rather than at the two callers: trimmed on the run path
        // and not on the Save site path was how the same form produced two different files. The
        // secret is in the list for a reason of its own as well - a trailing space pasted with it
        // is the difference between a token request that works and a 401 nobody can see the cause of.
        Assert.Equal("padded-password", profile.MemberPassword);
        Assert.Equal("padded-secret", profile.ClientSecret);

        // A different site is a different entry, which is the other half of the same rule.
        settings.Upsert(Profile("https://ndstk.se"));

        Assert.Equal(2, settings.Sites.Count);
    }
}
