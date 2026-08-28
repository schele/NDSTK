using System.Globalization;

namespace NDSTK.CookieScanner;

/// <summary>One past scan, as much of it as a list needs without loading the whole file.</summary>
public sealed record ScanHistoryEntry(
    string Path,
    DateTimeOffset CompletedAt,
    string Site,
    int EntryCount,
    int ExitCode);

/// <summary>
/// Keeps every scan's result on disk so two runs can be compared.
/// </summary>
/// <remarks>
/// The stored document is exactly the report's own JSON - a scan's findings are the record, so
/// there is no second format and no database to keep in step.
/// <para>
/// Both front ends write here, so a scan run from the command line shows up in the window's
/// history.
/// </para>
/// </remarks>
public sealed class ScanHistory(string folder)
{
    /// <summary>Kept small enough to read, large enough to cover a real working period.</summary>
    /// <remarks>
    /// By count rather than by age: "the last fifty scans" is comprehensible in a way "ninety
    /// days" is not when scanning happens irregularly.
    /// </remarks>
    public const int Keep = 50;

    public static ScanHistory Default() => new(DefaultFolder());

    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "scans");

    public static void Save(ScanResult result) => Default().SaveResult(result);

    public void SaveResult(ScanResult result)
    {
        Directory.CreateDirectory(folder);

        // The instant plus a short random suffix, so two scans finishing inside the same second
        // cannot overwrite one another. Sortable prefix so the filename alone orders the folder.
        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"{result.CompletedAt.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.json");

        File.WriteAllText(Path.Combine(folder, name), ScanJson.Serialize(result));

        // Pruned after the write, never before: a prune that fails must not cost the scan that
        // just finished.
        Prune();
    }

    /// <summary>Newest first. A file that will not parse is skipped, not fatal.</summary>
    public IReadOnlyList<ScanHistoryEntry> List()
    {
        if (Directory.Exists(folder) is false)
        {
            return [];
        }

        List<ScanHistoryEntry> entries = [];

        foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
        {
            ScanResult? result = Read(path);

            if (result is null)
            {
                continue;
            }

            entries.Add(new ScanHistoryEntry(
                path, result.CompletedAt, result.Site, result.Candidates.Count, result.ExitCode));
        }

        return [.. entries.OrderByDescending(entry => entry.CompletedAt)];
    }

    public ScanResult? Load(ScanHistoryEntry entry) => Read(entry.Path);

    private static ScanResult? Read(string path)
    {
        try
        {
            return ScanJson.Deserialize(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Prune()
    {
        List<ScanHistoryEntry> entries = [.. List()];

        foreach (ScanHistoryEntry stale in entries.Skip(Keep))
        {
            try
            {
                File.Delete(stale.Path);
            }
            catch (IOException)
            {
                // A file someone has open is not worth failing a completed scan over; the next
                // run prunes it.
            }
            catch (UnauthorizedAccessException)
            {
                // Same intent as the IOException case above: a read-only attribute, an ACL
                // denial, or an antivirus quarantine flag on a stale file must not cost the scan
                // that just finished.
            }
        }
    }
}
