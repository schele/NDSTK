using System.Text.Json;
using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// One saved site: everything the run card holds, for one URL.
/// </summary>
/// <remarks>
/// The URL is the identity and the label both - compared trimmed and ignoring case, shown in the
/// dropdown as it was typed. There is no name field on purpose: a profile called "test" beside one
/// called "staging" is two things to keep in step with the URLs they actually scan, and the URL is
/// the only one of the two that can be wrong in a way that matters.
/// <para>
/// The three credential fields are plaintext HERE and ciphertext on disk - see
/// <see cref="DashboardSettings.Save(string)"/>. Nothing in the window works with the encrypted form;
/// it exists between one write and the next read and nowhere else.
/// </para>
/// </remarks>
public sealed record SiteProfile(
    string Url,
    int MaxPages,
    Locale Locale,
    bool DryRun,
    // Defaulted so a message from the page that omits one arrives as an empty field rather than as a
    // null the non-nullable type above says cannot be there. System.Text.Json fills a missing
    // constructor parameter with its default rather than failing.
    string MemberEmail = "",
    string MemberPassword = "",
    string ClientId = "");

/// <summary>
/// What the window remembers between runs: the saved sites, and which one is showing.
/// </summary>
/// <remarks>
/// The member email, the member password and the API client id ARE persisted, and are encrypted at
/// rest with DPAPI under the Windows account that saved them (see <see cref="ProtectedText"/>). That
/// protects the file against another user on this machine, against another machine, and against the
/// file being copied somewhere else - all three produce a value that will not decrypt. It protects
/// nothing against code running as this user, which can ask DPAPI to open the blobs exactly as this
/// class does. It is at-rest protection for a convenience file, and it is worth having for that.
/// <para>
/// The client secret is still absent and must stay absent. The console tool refuses a
/// --client-secret flag so a secret cannot reach shell history; a settings file storing one would
/// undo that to save a paste, and unlike the three fields above there is a working alternative -
/// NDSTK_COOKIESCAN_CLIENT_SECRET - that costs the operator nothing per run. The member password had
/// no such alternative: it was retyped for every scan of a member area, which is the trade this
/// change reverses.
/// </para>
/// <para>
/// This is the one piece of shared mutable state in the window. <see cref="DashboardForm"/> holds a
/// single instance and hands the same one to <see cref="ScanSession"/>, because a run saves the
/// profile it ran with and the form saves the profile that was typed - two owners of one list. Both
/// run on the UI thread (the form's handlers are on the message loop, and the session mutates this
/// before it hands anything to a background task), so there is no lock and none is needed. If
/// anything here is ever touched from a scan's own thread, that stops being true.
/// </para>
/// </remarks>
public sealed record DashboardSettings(IReadOnlyList<SiteProfile> Sites, string? SelectedUrl)
{
    // Declared rather than left to the positional parameters, so the one instance the window shares
    // can be edited in place - see the class remark. A record's value equality still covers both,
    // which is what the round-trip test compares.
    public IReadOnlyList<SiteProfile> Sites { get; set; } = Sites ?? [];

    public string? SelectedUrl { get; set; } = SelectedUrl;

    /// <summary>
    /// One line per credential that could not be decrypted, for the page to show in its log.
    /// </summary>
    /// <remarks>
    /// Carried on the settings rather than thrown or logged from <see cref="Load(string)"/> itself,
    /// because Load runs from a field initializer before the window - let alone the page's log panel
    /// - exists. Not serialised: it describes one read of one file, not something to remember.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    // Save and Load must share one instance: the same latent bug the team deliberately fixed in
    // ScanJson otherwise reappears here - Locale would serialise as an integer, and reordering the
    // enum would silently change a saved setting's meaning. JsonStringEnumConverter reads numbers
    // back as well as names by default (only allowIntegerValues: false would turn that off, and
    // this does not set it), so an existing settings file holding "Locale": 0 still loads.
    //
    // PropertyNameCaseInsensitive is here for the same class of problem one level up: the file is
    // written in the property names' own casing, but it is also a file people open and edit, and a
    // hand-typed "sites" that loaded as no sites at all would look exactly like a file that had been
    // wiped.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "settings.json");

    /// <summary>No saved sites - a first launch, or a file that would not read.</summary>
    private static DashboardSettings Empty => new([], null);

    /// <summary>What the window remembers, from the file the window keeps it in.</summary>
    public static DashboardSettings Load() => Load(DefaultPath);

    /// <summary>The same, from a named file.</summary>
    /// <remarks>
    /// The overload exists for the tests, which must not touch
    /// <c>%LOCALAPPDATA%\NDSTK.CookieScanner\settings.json</c> - it is the operator's real list of
    /// sites, and a suite that wrote over it would be destroying data to prove it can save.
    /// </remarks>
    public static DashboardSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) is false)
            {
                return Empty;
            }

            string json = File.ReadAllText(path);

            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return Empty;
            }

            // The discriminator between the two shapes is the presence of the array, not a version
            // number: a version field would be one more thing a hand-edited file can get wrong, and
            // the shapes are already told apart by what they contain.
            bool hasSites = document.RootElement.EnumerateObject().Any(
                property => string.Equals(property.Name, nameof(Sites), StringComparison.OrdinalIgnoreCase));

            return hasSites ? Opened(json) : Migrated(json);
        }
        catch (Exception)
        {
            // Unreadable settings are not worth refusing to start over, and the catch is broad
            // deliberately: this runs from a field initializer, on the constructor path, where
            // anything that escapes means no window at all rather than a window with defaults in
            // it. A remembered site is a convenience; nothing about reading one earns that risk.
            return Empty;
        }
    }

    /// <summary>Writes what the window remembers, credentials encrypted, to the window's own file.</summary>
    public void Save() => Save(DefaultPath);

    /// <summary>The same, to a named file - see <see cref="Load(string)"/> for why the overload exists.</summary>
    public void Save(string path)
    {
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(folder) is false)
            {
                Directory.CreateDirectory(folder);
            }

            // A copy, encrypted on the way out. The instance this was called on stays plaintext: it
            // is the one the form and the running scan are both reading from, and swapping its
            // fields for ciphertext for the length of a write would be a race with the next reader
            // rather than a saving.
            DashboardSettings written = new([.. Sites.Select(Protected)], SelectedUrl);

            File.WriteAllText(path, JsonSerializer.Serialize(written, Options));
        }
        catch (Exception)
        {
            // Losing the remembered settings is a nuisance, not a reason to fail a scan. Broad for
            // the same reason as Load, and for one more: the caller invokes this outside its own
            // try, so anything that escapes here would reach the message loop unhandled.
            //
            // DPAPI's own failures land here too, and the whole write is what they cost: Protected
            // runs over every profile before a byte is written, so one field that will not encrypt
            // abandons the save and leaves the file exactly as it was. That is the right end of the
            // trade - the alternative to an unchanged file is a half-written one - but it does mean
            // a profile is never quietly written in the clear, and never quietly written alone.
        }
    }

    /// <summary>Adds a profile, or replaces the one already saved for its URL.</summary>
    /// <remarks>
    /// Replaced in place rather than removed and appended, so editing a saved site does not move it
    /// to the bottom of a dropdown the operator has learned the order of.
    /// <para>
    /// Editing the URL of a profile and saving is therefore a "save as": the new URL matches nothing,
    /// so a second profile appears and the original stays until it is deleted. That is the useful
    /// reading - copying a set of options from staging to production is the common case, and a
    /// window that silently renamed the original would have destroyed the profile it was copied from.
    /// </para>
    /// <para>
    /// Every stored string is trimmed HERE rather than by the callers. There are two of them - the
    /// Save site button and a run saving what it ran with - and while the URL had to be normalised at
    /// this end anyway, since it is what the match above compares, the credentials did not: they were
    /// trimmed on one path and not the other, so the same form produced two different files depending
    /// on which button had been pressed. One place, both callers, no way for them to disagree.
    /// </para>
    /// </remarks>
    public void Upsert(SiteProfile profile)
    {
        SiteProfile stored = profile with
        {
            Url = Trimmed(profile.Url),
            MemberEmail = Trimmed(profile.MemberEmail),
            MemberPassword = Trimmed(profile.MemberPassword),
            ClientId = Trimmed(profile.ClientId),
        };

        List<SiteProfile> next = [.. Sites];

        int index = next.FindIndex(candidate => IsSameSite(candidate.Url, stored.Url));

        if (index < 0)
        {
            next.Add(stored);
        }
        else
        {
            next[index] = stored;
        }

        Sites = next;
    }

    /// <summary>Drops the profile saved for a URL, if there is one.</summary>
    public void Remove(string url)
        => Sites = [.. Sites.Where(candidate => IsSameSite(candidate.Url, url) is false)];

    /// <summary>
    /// Whether two URLs name the same profile.
    /// </summary>
    /// <remarks>
    /// Trimmed and case-insensitive, because both are ways one site spells itself differently: a URL
    /// pasted with a trailing space, or typed with a capital in the host. Neither is a second site,
    /// and two dropdown entries that read identically is a list nobody can use. Deliberately no more
    /// than that - a trailing slash IS a different string here, unlike the history's site key, because
    /// this is a value the operator typed and gets shown back rather than one Uri produced.
    /// </remarks>
    private static bool IsSameSite(string? left, string? right)
        => string.Equals(Trimmed(left), Trimmed(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One stored string, normalised: never null, never padded.
    /// </summary>
    /// <remarks>
    /// The null half is not paranoia about this project's own code - the records say these are not
    /// nullable - but about the page's: System.Text.Json fills a member a message omits with the
    /// parameter's default, and an explicit <c>"memberEmail": null</c> arrives as a null the type
    /// says cannot be there.
    /// </remarks>
    private static string Trimmed(string? value) => (value ?? "").Trim();

    /// <summary>The new shape: profiles as stored, with the three blobs opened.</summary>
    /// <remarks>
    /// A profile with a blank URL is dropped before anything else happens to it. The URL is the
    /// identity, the label and the delete key all at once, so a blank one is a row the dropdown
    /// renders with the same empty value as its "New site" option: picking it does nothing, Delete
    /// stays disabled because nothing is selected, and it survives every save. Unreachable and
    /// unremovable is worse than absent, and there is nothing in such a profile worth keeping - it
    /// cannot name a site to scan. Filtered before the decrypt so it does not spend warnings either.
    /// </remarks>
    private static DashboardSettings Opened(string json)
    {
        DashboardSettings stored = JsonSerializer.Deserialize<DashboardSettings>(json, Options) ?? Empty;

        List<string> warnings = [];

        stored.Sites =
        [
            .. stored.Sites.Where(profile => Trimmed(profile.Url).Length > 0).Select(profile => profile with
            {
                MemberEmail = Revealed(profile.Url, "member email", profile.MemberEmail, warnings),
                MemberPassword = Revealed(profile.Url, "member password", profile.MemberPassword, warnings),
                ClientId = Revealed(profile.Url, "API client id", profile.ClientId, warnings),
            }),
        ];

        stored.Warnings = warnings;

        return stored;
    }

    /// <summary>One stored credential, or an empty field and a line saying which one went.</summary>
    /// <remarks>
    /// Never a throw and never a dropped profile. The URL, the page count, the locale and the dry-run
    /// flag in a profile whose password will not open are all still exactly right, and they are most
    /// of what the profile is for.
    /// </remarks>
    private static string Revealed(string url, string field, string stored, List<string> warnings)
    {
        if (ProtectedText.TryUnprotect(stored, out string value))
        {
            return value;
        }

        warnings.Add(
            $"The saved {field} for {url} could not be read on this machine, so that field is empty. " +
            "Settings are encrypted for one Windows user on one machine; type it again and save the site.");

        return "";
    }

    /// <summary>The pre-profiles file: one set of flat fields, read as the one site it described.</summary>
    /// <remarks>
    /// Converted on read and written back in the new shape by the next save, rather than rewritten
    /// here: Load runs on the constructor path, and a migration that wrote to disk from there would
    /// turn a read-only settings folder into a window that fails to open.
    /// <para>
    /// The password is empty because the old file never held one - it was typed per run, which is
    /// exactly the behaviour profiles replace. Nothing is warned about: an empty field is the truth
    /// about what that file contained, not a value that was lost.
    /// </para>
    /// </remarks>
    private static DashboardSettings Migrated(string json)
    {
        Flat flat = JsonSerializer.Deserialize<Flat>(json, Options) ?? new Flat();

        string url = Trimmed(flat.Url);

        // A blank URL migrates to nothing at all, rather than to a profile that cannot be picked,
        // scanned or deleted - see the remark on Opened for what such a row does to the dropdown.
        // The old file allowed one: its Url was whatever had last been typed, and clearing the field
        // and running a scan that was then refused left it empty. There is nothing to migrate there;
        // the page count and locale it would carry are the defaults a new site starts with anyway.
        if (url.Length == 0)
        {
            return Empty;
        }

        SiteProfile profile = new(
            Url: url,
            MaxPages: flat.MaxPages,
            Locale: flat.Locale,
            DryRun: flat.DryRun,
            MemberEmail: Trimmed(flat.MemberEmail),
            MemberPassword: "",
            ClientId: Trimmed(flat.ClientId));

        // Selected, not merely present: the whole point of the old file was to reopen on the site it
        // named, and a migrated profile sitting unselected behind "New site" would look like the
        // settings had been lost.
        return new DashboardSettings([profile], profile.Url);
    }

    /// <summary>A copy with the three credential fields turned into blobs.</summary>
    private static SiteProfile Protected(SiteProfile profile) => profile with
    {
        MemberEmail = ProtectedText.Protect(profile.MemberEmail),
        MemberPassword = ProtectedText.Protect(profile.MemberPassword),
        ClientId = ProtectedText.Protect(profile.ClientId),
    };

    /// <summary>
    /// The settings file as the pre-profiles build wrote it.
    /// </summary>
    /// <remarks>
    /// Kept as its own record rather than read field by field out of a JsonDocument, so the defaults
    /// that build shipped with are still spelled out here: a file missing a key must migrate to what
    /// that build would have shown for it, not to a zero.
    /// </remarks>
    private sealed record Flat(
        string Url = "https://localhost:44351",
        int MaxPages = 25,
        Locale Locale = Locale.Sv,
        string MemberEmail = "",
        string ClientId = "",
        bool DryRun = true);
}
