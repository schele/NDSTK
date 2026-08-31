using System.Text.Json;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// One message from the page to the host, already turned into the command it names.
/// </summary>
/// <remarks>
/// Both directions speak <see cref="ScanJson.Options"/> - the same camelCase, enums-as-names dialect
/// the report file is written in - so there is one place to change how the two sides talk, and a
/// <c>ScanResult</c> posted to the page is byte-for-byte the document the report holds.
/// </remarks>
public abstract record DashboardCommand
{
    /// <summary>
    /// Reads the <c>type</c> discriminator and deserialises the record it names, or returns null.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, for anything unrecognised or unparseable. The page is inside
    /// the exe, so a message this method cannot read is a bug rather than an attack - but it arrives
    /// on the WebView2 message loop, where an exception takes the loop down and with it every later
    /// message. A dropped message is the smaller failure.
    /// </remarks>
    public static DashboardCommand? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || document.RootElement.TryGetProperty("type", out JsonElement type) is false
                || type.ValueKind is not JsonValueKind.String)
            {
                return null;
            }

            return type.GetString() switch
            {
                // The records carrying nothing are constructed rather than deserialised: there is
                // no payload to read, and a constructor cannot fail on a member that is not there.
                "cancel" => new CancelCommand(),
                "listHistory" => new ListHistoryCommand(),
                "ready" => new ReadyCommand(),
                "run" => JsonSerializer.Deserialize<RunCommand>(json, ScanJson.Options),
                "loadScan" => JsonSerializer.Deserialize<LoadScanCommand>(json, ScanJson.Options),
                "compare" => JsonSerializer.Deserialize<CompareCommand>(json, ScanJson.Options),
                "saveSite" => CompleteSave(JsonSerializer.Deserialize<SaveSiteCommand>(json, ScanJson.Options)),
                "deleteSite" => CompleteDelete(JsonSerializer.Deserialize<DeleteSiteCommand>(json, ScanJson.Options)),
                "deleteScan" => CompleteDeleteScan(JsonSerializer.Deserialize<DeleteScanCommand>(json, ScanJson.Options)),
                "clearScans" => new ClearScansCommand(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }

        // System.Text.Json fills a constructor parameter the message does not carry with default, so
        // a saveSite with no `profile` - or a deleteSite with no `url` - deserialises into a command
        // holding a null the record's own type says cannot be there. Checked here rather than at the
        // handlers because these are the commands that end in a write: every other command's
        // missing member costs a scan, and a deleteSite with no URL would match no profile, remove
        // nothing, drop the selection and rewrite the file to say so.
        //
        // deleteScan is here for the same reason and a sharper one: it ends in File.Delete. A null
        // path matches nothing in ScanHistory.Delete's own listing check, so both guards would have
        // to fail before anything happened - which is the point of having both.
        //
        // Separate names rather than one overloaded set, because local functions cannot be overloaded.
        static SaveSiteCommand? CompleteSave(SaveSiteCommand? command)
            => command is { Profile: not null } ? command : null;

        static DeleteSiteCommand? CompleteDelete(DeleteSiteCommand? command)
            => command is { Url: not null } ? command : null;

        static DeleteScanCommand? CompleteDeleteScan(DeleteScanCommand? command)
            => command is { Path: not null } ? command : null;
    }
}

/// <summary>Start a scan with the options the page currently shows.</summary>
/// <remarks>
/// <c>Locale</c> is the enum's name as a string rather than the enum itself: this record is the wire
/// format, and a page sending a locale this build has never heard of should be one warning line
/// rather than a message the whole loop cannot parse.
/// <para>
/// A run carries the fields the form currently shows rather than the name of a saved profile, even
/// though the two are usually the same: what runs must be what is on screen, and a run that fetched
/// its own options from the settings would scan something other than what the operator was reading.
/// The profile is written FROM the run afterwards - see <see cref="ScanSession"/> - never the other
/// way round.
/// </para>
/// <para>
/// <c>ClientSecret</c> is nullable like the credentials beside it, and the distinction costs more
/// here than for any of them: <see cref="ScanSession.StartAsync"/> lets the machine's environment
/// variable fill in exactly when this is blank. "The page sent no field" and "the page sent an empty
/// secret" therefore have to mean the same thing - and they do, because both are what a run started
/// with an empty box looks like, and both are the case the fallback exists for.
/// </para>
/// </remarks>
public sealed record RunCommand(
    string Url, int MaxPages, string Locale, string? MemberEmail,
    string? MemberPassword, string? ClientId, string? ClientSecret, bool DryRun) : DashboardCommand;

/// <summary>Save the run card's current values as the profile for the URL they name.</summary>
/// <remarks>
/// The whole profile travels in one member rather than as eight loose fields, so the record the page
/// sends and the record the file stores are the same type - a field added to
/// <see cref="SiteProfile"/> later reaches the page's message without a second declaration to keep
/// in step. Its <c>Locale</c> is therefore the enum itself, unlike <see cref="RunCommand"/>'s: a
/// spelling this build cannot read is worth refusing at the parse when the next thing that happens
/// is a write to disk.
/// </remarks>
public sealed record SaveSiteCommand(SiteProfile Profile) : DashboardCommand;

/// <summary>Forget the profile saved for one URL.</summary>
public sealed record DeleteSiteCommand(string Url) : DashboardCommand;

/// <summary>Delete one kept scan, by the path a history answer gave the page.</summary>
/// <remarks>
/// The path is not trusted on arrival: <see cref="ScanHistory.Delete"/> matches it against the
/// folder's own listing first, so the only paths this can reach are ones the host itself reported.
/// </remarks>
public sealed record DeleteScanCommand(string Path) : DashboardCommand;

/// <summary>Delete every kept scan. The page asks the operator first; the host does not.</summary>
public sealed record ClearScansCommand : DashboardCommand;

public sealed record CancelCommand : DashboardCommand;
public sealed record ListHistoryCommand : DashboardCommand;
public sealed record LoadScanCommand(string Path) : DashboardCommand;
public sealed record CompareCommand(string PathA, string PathB) : DashboardCommand;
public sealed record ReadyCommand : DashboardCommand;

/// <summary>
/// The envelopes the host sends back that more than one place has to build.
/// </summary>
/// <remarks>
/// Only <c>sites</c> qualifies today: the form answers <c>saveSite</c> and <c>deleteSite</c> with it,
/// and <see cref="ScanSession"/> posts it as a run STARTS - right after the upsert that records what
/// the run is about to do, and long before the run finishes. Two anonymous objects spelling the same
/// envelope in two files is exactly the drift the page cannot be told about - a renamed member would
/// leave the dropdown empty after a run and full after a save, with nothing to compile against. Every
/// other answer is built where it is posted, because every other answer has one caller.
/// </remarks>
public static class DashboardAnswer
{
    public static object Sites(DashboardSettings settings)
        => new { type = "sites", sites = settings.Sites, selectedUrl = settings.SelectedUrl };
}
