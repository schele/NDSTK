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
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Start a scan with the options the page currently shows.</summary>
/// <remarks>
/// <c>Locale</c> is the enum's name as a string rather than the enum itself: this record is the wire
/// format, and a page sending a locale this build has never heard of should be one warning line
/// rather than a message the whole loop cannot parse.
/// <para>
/// There is a member password here and none in <see cref="DashboardSettings"/>, deliberately: it is
/// typed per run and lives only as long as the run does.
/// </para>
/// </remarks>
public sealed record RunCommand(
    string Url, int MaxPages, string Locale, string? MemberEmail,
    string? MemberPassword, string? ClientId, bool DryRun) : DashboardCommand;

public sealed record CancelCommand : DashboardCommand;
public sealed record ListHistoryCommand : DashboardCommand;
public sealed record LoadScanCommand(string Path) : DashboardCommand;
public sealed record CompareCommand(string PathA, string PathB) : DashboardCommand;
public sealed record ReadyCommand : DashboardCommand;
