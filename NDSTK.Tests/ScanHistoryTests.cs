using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ScanHistoryTests : IDisposable
{
    private readonly string folder =
        Path.Combine(Path.GetTempPath(), "ndstk-scan-history-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static ScanResult Result(DateTimeOffset completedAt, int candidates = 1)
        => new(
            Candidates: [.. Enumerable.Range(0, candidates).Select(index =>
                new CookieDeclarationCandidate($"cookie{index}", "Denna webbplats", "necessary",
                    "Syfte.", "Session", "Cookie", CandidateFlag.None, ConsentPass.Undecided,
                    "https://ndstk.se/"))],
            Violations: [],
            ExpectedButNotObserved: [],
            HostsByPass: new Dictionary<ConsentPass, IReadOnlyList<string>>(),
            Outcome: null,
            CanReachApi: false,
            DryRun: false,
            CompletedAt: completedAt,
            Site: "https://ndstk.se/");

    [Fact]
    public void A_saved_scan_can_be_listed_and_loaded_back()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), candidates: 3));

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Single(entries);
        Assert.Equal("https://ndstk.se/", entries[0].Site);
        Assert.Equal(3, entries[0].EntryCount);
        Assert.Equal(0, entries[0].ExitCode);

        ScanResult? loaded = history.Load(entries[0]);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Candidates.Count);
    }

    // Newest first, because "what did the last scan say" is the question asked most often.
    [Fact]
    public void Entries_are_listed_newest_first()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero)));
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero)));

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Equal(3, entries.Count);
        Assert.Equal(28, entries[0].CompletedAt.Day);
        Assert.Equal(27, entries[1].CompletedAt.Day);
        Assert.Equal(26, entries[2].CompletedAt.Day);
    }

    // The folder must not grow without limit on a machine that scans often.
    [Fact]
    public void The_folder_is_pruned_to_the_most_recent_fifty()
    {
        var history = new ScanHistory(folder);
        DateTimeOffset first = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // AddDays rather than the raw day-of-month: 55 sequential days runs past January's 31,
        // which the constructor cannot represent directly.
        for (int day = 1; day <= 55; day++)
        {
            history.SaveResult(Result(first.AddDays(day - 1)));
        }

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Equal(50, entries.Count);
        // The five oldest went, not the five newest.
        Assert.Equal(first.AddDays(54), entries[0].CompletedAt);
        Assert.Equal(first.AddDays(5), entries[^1].CompletedAt);
    }

    // The folder holds files this code did not necessarily write. One unreadable file must cost
    // its own row, not the whole list - a history browser that throws on startup is useless.
    [Fact]
    public void An_unparseable_file_is_skipped_rather_than_failing_the_list()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));
        File.WriteAllText(Path.Combine(folder, "20260101-000000-junk.json"), "not json at all");

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Single(entries);
    }

    // A file that is well-formed JSON but the wrong shape - the exact hand-edit mistake ScanJson
    // guards against - must be skipped the same way a syntactically bad file is, not throw and
    // cost the whole list.
    [Fact]
    public void A_well_formed_but_shape_invalid_file_is_skipped_rather_than_failing_the_list()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));
        File.WriteAllText(Path.Combine(folder, "20260101-000000-empty.json"), "{}");

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Single(entries);
    }

    [Fact]
    public void Listing_an_absent_folder_is_empty_rather_than_an_error()
    {
        Assert.Empty(new ScanHistory(Path.Combine(folder, "never-created")).List());
    }

    // Two scans finishing in the same second must not overwrite each other.
    [Fact]
    public void Two_scans_at_the_same_instant_produce_two_entries()
    {
        var history = new ScanHistory(folder);
        DateTimeOffset instant = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

        history.SaveResult(Result(instant));
        history.SaveResult(Result(instant));

        Assert.Equal(2, history.List().Count);
    }

    // The trend chart needs a violation count per scan. List() already parses every file, so the
    // count is free here and would otherwise cost a second read of all fifty.
    [Fact]
    public void An_entry_carries_the_violation_count()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero), candidates: 3) with
        {
            Violations =
            [
                new CookieDeclarationCandidate("_fbp", "Meta", "marketing", "Annonser.", "3 månader",
                    "Cookie", CandidateFlag.Violation, ConsentPass.RejectAll, "https://ndstk.se/"),
            ],
        });

        ScanHistoryEntry entry = Assert.Single(history.List());

        Assert.Equal(3, entry.EntryCount);
        Assert.Equal(1, entry.ViolationCount);
        Assert.Equal(1, entry.ExitCode);
    }

    [Fact]
    public void One_kept_scan_can_be_deleted_and_the_rest_stay()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero)));
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));

        ScanHistoryEntry newest = history.List()[0];

        Assert.True(history.Delete(newest.Path));
        Assert.False(File.Exists(newest.Path));

        ScanHistoryEntry remaining = Assert.Single(history.List());

        Assert.Equal(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero), remaining.CompletedAt);
    }

    // The path arrives from script inside a WebView. Delete matches it against the folder's own
    // listing first, so this is the assertion that keeps it from being a file-delete primitive the
    // page can aim anywhere - the one thing a later refactor of Delete must not quietly drop.
    [Fact]
    public void A_path_outside_the_history_folder_is_refused_and_left_alone()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));

        string outsider = Path.Combine(Path.GetTempPath(), "ndstk-not-history-" + Guid.NewGuid().ToString("N") + ".json");

        File.WriteAllText(outsider, "{}");

        try
        {
            Assert.False(history.Delete(outsider));
            Assert.True(File.Exists(outsider));
            Assert.Single(history.List());
        }
        finally
        {
            File.Delete(outsider);
        }
    }

    [Fact]
    public void Clearing_deletes_every_kept_scan()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero)));
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero)));
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));

        Assert.Equal(3, history.DeleteAll());
        Assert.Empty(history.List());
    }

    [Fact]
    public void Deleting_a_path_that_was_never_kept_reports_false()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));

        Assert.False(history.Delete(Path.Combine(folder, "20260828-100000-deadbeef.json")));
        Assert.Single(history.List());
    }
}
