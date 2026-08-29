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
