using System.Text.Json;

using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;
using NDSTK.CookieScanner.Desktop;

namespace NDSTK.Desktop.Tests;

public class DashboardMessageTests
{
    [Fact]
    public void A_run_command_parses_with_all_its_options()
    {
        const string json = """
            {"type":"run","url":"https://localhost:44351","maxPages":7,"locale":"En",
             "memberEmail":"a@b.c","memberPassword":"secret","clientId":"cookie-scanner","dryRun":false}
            """;

        DashboardCommand? command = DashboardCommand.Parse(json);

        RunCommand run = Assert.IsType<RunCommand>(command);

        Assert.Equal("https://localhost:44351", run.Url);
        Assert.Equal(7, run.MaxPages);
        Assert.Equal("En", run.Locale);
        Assert.Equal("secret", run.MemberPassword);
        Assert.False(run.DryRun);
    }

    [Fact]
    public void A_cancel_command_parses()
    {
        Assert.IsType<CancelCommand>(DashboardCommand.Parse("""{"type":"cancel"}"""));
    }

    [Fact]
    public void A_load_scan_command_parses_with_its_path()
    {
        const string json = """{"type":"loadScan","path":"C:\\scans\\one.json"}""";

        DashboardCommand? command = DashboardCommand.Parse(json);

        LoadScanCommand load = Assert.IsType<LoadScanCommand>(command);

        Assert.Equal(@"C:\scans\one.json", load.Path);
    }

    [Fact]
    public void A_compare_command_parses_with_both_paths()
    {
        const string json = """{"type":"compare","pathA":"C:\\scans\\one.json","pathB":"C:\\scans\\two.json"}""";

        DashboardCommand? command = DashboardCommand.Parse(json);

        CompareCommand compare = Assert.IsType<CompareCommand>(command);

        Assert.Equal(@"C:\scans\one.json", compare.PathA);
        Assert.Equal(@"C:\scans\two.json", compare.PathB);
    }

    [Fact]
    public void A_list_history_command_parses()
    {
        Assert.IsType<ListHistoryCommand>(DashboardCommand.Parse("""{"type":"listHistory"}"""));
    }

    /// <summary>
    /// The whole profile, exactly as the run card sends it.
    /// </summary>
    /// <remarks>
    /// The locale is the enum's NAME here and the enum's type on the record, unlike
    /// <see cref="RunCommand"/> where it stays a string: a profile is what gets written to disk, so a
    /// spelling this build cannot read is worth finding out about at the parse rather than storing
    /// and quietly defaulting later. <see cref="ScanJson.Options"/> is what makes both halves of that
    /// work - camelCase off the page, enums as names.
    /// </remarks>
    [Fact]
    public void A_save_site_command_parses_with_its_whole_profile()
    {
        const string json = """
            {"type":"saveSite","profile":{"url":"https://localhost:44351","maxPages":7,"locale":"En",
             "dryRun":false,"memberEmail":"a@b.c","memberPassword":"secret","clientId":"cookie-scanner"}}
            """;

        DashboardCommand? command = DashboardCommand.Parse(json);

        SaveSiteCommand save = Assert.IsType<SaveSiteCommand>(command);

        Assert.Equal("https://localhost:44351", save.Profile.Url);
        Assert.Equal(7, save.Profile.MaxPages);
        Assert.Equal(Locale.En, save.Profile.Locale);
        Assert.False(save.Profile.DryRun);
        Assert.Equal("a@b.c", save.Profile.MemberEmail);
        Assert.Equal("secret", save.Profile.MemberPassword);
        Assert.Equal("cookie-scanner", save.Profile.ClientId);
    }

    [Fact]
    public void A_delete_site_command_parses_with_its_url()
    {
        DashboardCommand? command = DashboardCommand.Parse(
            """{"type":"deleteSite","url":"https://localhost:44351"}""");

        DeleteSiteCommand delete = Assert.IsType<DeleteSiteCommand>(command);

        Assert.Equal("https://localhost:44351", delete.Url);
    }

    /// <summary>
    /// The names the diff view reads a comparison by.
    /// </summary>
    /// <remarks>
    /// Same reasoning as the history entry's test below, and the same blind spot it covers: the view
    /// is JavaScript inside an embedded resource, so a member renamed on either of these two records
    /// would compile cleanly and leave the page reading undefined - a recategorisation row with three
    /// empty cells, or an options banner announcing a difference it cannot name. Both records live
    /// outside this project, which is exactly why the page's dependency on their spelling is pinned
    /// from here.
    /// </remarks>
    [Fact]
    public void A_diff_payload_serialises_under_the_names_the_page_reads()
    {
        string json = JsonSerializer.Serialize(
            new
            {
                recategorised = new[] { new CategoryChange("ndstk-consent", "marketing", "necessary") },
                options = new ScanOptionsSummary(7, Locale.En, MemberScanEnabled: true, DryRun: false),
            },
            ScanJson.Options);

        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement change = document.RootElement.GetProperty("recategorised")[0];

        Assert.Equal("ndstk-consent", change.GetProperty("name").GetString());
        Assert.Equal("marketing", change.GetProperty("from").GetString());
        Assert.Equal("necessary", change.GetProperty("to").GetString());

        JsonElement options = document.RootElement.GetProperty("options");

        Assert.Equal(7, options.GetProperty("maxPages").GetInt32());
        // A name, not the enum's number: the banner prints this value straight into its sentence.
        Assert.Equal("En", options.GetProperty("locale").GetString());
        Assert.True(options.GetProperty("memberScanEnabled").GetBoolean());
        Assert.False(options.GetProperty("dryRun").GetBoolean());
    }

    /// <summary>
    /// The names the trend chart reads a history entry by.
    /// </summary>
    /// <remarks>
    /// The chart is JavaScript inside an embedded resource, so nothing the compiler does can catch a
    /// renamed member here: the page would simply plot zeroes for a series whose key had moved. This
    /// pins the six names the page actually indexes, through the same options the host posts with.
    /// </remarks>
    [Fact]
    public void A_history_entry_serialises_under_the_names_the_page_reads()
    {
        var entry = new ScanHistoryEntry(
            @"C:\scans\20260829-030943-f2dc8ed6.json",
            new DateTimeOffset(2026, 8, 29, 3, 9, 43, TimeSpan.Zero),
            "https://localhost:44351/",
            EntryCount: 3,
            ViolationCount: 1,
            ExitCode: 1);

        string json = JsonSerializer.Serialize(new { type = "history", entries = new[] { entry } }, ScanJson.Options);

        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement posted = document.RootElement.GetProperty("entries")[0];

        Assert.Equal("history", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(@"C:\scans\20260829-030943-f2dc8ed6.json", posted.GetProperty("path").GetString());
        Assert.Equal("https://localhost:44351/", posted.GetProperty("site").GetString());
        Assert.Equal(3, posted.GetProperty("entryCount").GetInt32());
        Assert.Equal(1, posted.GetProperty("violationCount").GetInt32());
        Assert.Equal(1, posted.GetProperty("exitCode").GetInt32());

        // Read by `new Date(...)` on the page, so it has to be a string it can parse rather than
        // .NET's own tuple-ish shapes.
        Assert.Equal(
            new DateTimeOffset(2026, 8, 29, 3, 9, 43, TimeSpan.Zero),
            posted.GetProperty("completedAt").GetDateTimeOffset());
    }

    // The page is inside the exe, so an unknown type is a bug rather than an attack - but throwing
    // here would take down the message loop, and a dropped message is the smaller failure.
    [Fact]
    public void An_unknown_type_is_ignored_rather_than_throwing()
    {
        Assert.Null(DashboardCommand.Parse("""{"type":"launch-missiles"}"""));
    }

    [Fact]
    public void Malformed_json_is_ignored_rather_than_throwing()
    {
        Assert.Null(DashboardCommand.Parse("not json"));
    }
}
