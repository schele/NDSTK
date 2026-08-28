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
    public static ScanResult? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScanResult>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
