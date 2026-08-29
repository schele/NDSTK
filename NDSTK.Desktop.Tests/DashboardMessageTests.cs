using System.Text.Json;

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
    public void A_list_history_command_parses()
    {
        Assert.IsType<ListHistoryCommand>(DashboardCommand.Parse("""{"type":"listHistory"}"""));
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
