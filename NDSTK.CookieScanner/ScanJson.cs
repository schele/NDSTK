using System.Text.Json;
using System.Text.Json.Serialization;

namespace NDSTK.CookieScanner;

/// <summary>
/// The one place a <see cref="ScanResult"/> is turned into JSON and back.
/// </summary>
/// <remarks>
/// Shared by the report file and the history folder so the two cannot drift into different
/// shapes - history reads the same document the report writes.
/// <para>
/// Enums are written as names rather than integers: the file is meant to be readable, and an
/// integer would silently change meaning if a <c>ConsentPass</c> member were ever reordered.
/// </para>
/// </remarks>
public static class ScanJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ScanResult result) => JsonSerializer.Serialize(result, Options);

    /// <summary>
    /// Returns null for anything that will not parse, rather than throwing: the history browser
    /// lists a folder of files it did not necessarily write, and one bad file must not cost the
    /// whole list.
    /// </summary>
    /// <remarks>
    /// Well-formed JSON of the wrong shape is just as unparseable as bad syntax: System.Text.Json
    /// fills a record's missing constructor parameters with <c>default</c> rather than failing, so
    /// <c>{}</c> or a mistyped key (<c>"Candidate"</c> for <c>"candidates"</c>) comes back as a
    /// <see cref="ScanResult"/> whose collections are null instead of throwing here. Left
    /// unchecked, that null surfaces later and further away - <see cref="ScanResult.ExitCode"/>
    /// throwing a <see cref="NullReferenceException"/> - so the shape is validated before the
    /// result is handed back.
    /// </remarks>
    public static ScanResult? Deserialize(string json)
    {
        try
        {
            ScanResult? result = JsonSerializer.Deserialize<ScanResult>(json, Options);

            return result is
            {
                Site: not null,
                Candidates: not null,
                Violations: not null,
                ExpectedButNotObserved: not null,
                HostsByPass: not null,
            }
                ? result
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
