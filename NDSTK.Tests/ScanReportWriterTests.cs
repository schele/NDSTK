using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ScanReportWriterTests
{
    private static readonly Guid PolicyPageKey = new("11111111-2222-3333-4444-555555555555");

    private static ScanOptions Options(bool dryRun) => new(
        Url: new Uri("https://ndstk.se/"),
        Target: new Uri("https://ndstk.se/"),
        MaxPages: 25,
        Locale: Locale.Sv,
        MemberEmail: null,
        MemberPassword: null,
        ClientId: "cookie-scanner",
        ClientSecret: "secret-for-this-test-only",
        DryRun: dryRun,
        ReportDir: Path.GetTempPath(),
        Headed: false);

    private static ScanResult Result(bool dryRun, bool saved) => new(
        Candidates:
        [
            new("_ga_*", "Google Analytics", "statistics", "Mäter.", "24 månader", "Cookie",
                CandidateFlag.NeedsReview, ConsentPass.AcceptAll, "https://ndstk.se/"),
        ],
        Violations: [],
        ExpectedButNotObserved: [],
        HostsByPass: new Dictionary<ConsentPass, IReadOnlyList<string>>(),
        Outcome: new MergeOutcome(["_ga_*", "ndstk-consent"], [], [], PolicyPageKey, saved),
        CanReachApi: true,
        DryRun: dryRun,
        CompletedAt: new DateTimeOffset(2026, 8, 30, 15, 57, 0, TimeSpan.Zero),
        Site: "https://ndstk.se/");

    // The line that misled an operator: a dry run reported "2 added" and nothing had been added
    // anywhere. The summary is what the console prints and what the dashboard's log shows, so it
    // has to say what actually happened, not what would have.
    [Fact]
    public void A_dry_run_says_what_would_be_added_and_that_nothing_changed()
    {
        IReadOnlyList<string> lines = ScanReportWriter.SummaryLines(Options(dryRun: true), Result(dryRun: true, saved: false));

        Assert.Contains(lines, line => line.Contains("2 would be added, 0 already declared, 0 declared but not found."));
        Assert.Contains(lines, line => line.Contains("Dry run - the policy page was not changed."));
        Assert.DoesNotContain(lines, line => line.Contains("2 added,"));
        Assert.DoesNotContain(lines, line => line.Contains("saved as a DRAFT"));
    }

    [Fact]
    public void A_saved_merge_says_added_and_names_the_draft()
    {
        IReadOnlyList<string> lines = ScanReportWriter.SummaryLines(Options(dryRun: false), Result(dryRun: false, saved: true));

        Assert.Contains(lines, line => line.Contains("2 added, 0 already declared, 0 declared but not found."));
        Assert.Contains(lines, line => line.Contains($"The policy page ({PolicyPageKey}) was saved as a DRAFT."));
        Assert.DoesNotContain(lines, line => line.Contains("Dry run"));
    }

    // A real run that had nothing to write is neither a dry run nor a save; saying so stops the
    // operator wondering which of the two it was.
    [Fact]
    public void A_real_run_with_nothing_to_write_says_so()
    {
        IReadOnlyList<string> lines = ScanReportWriter.SummaryLines(Options(dryRun: false), Result(dryRun: false, saved: false));

        Assert.Contains(lines, line => line.Contains("Nothing new to write"));
        Assert.DoesNotContain(lines, line => line.Contains("Dry run"));
        Assert.DoesNotContain(lines, line => line.Contains("saved as a DRAFT"));
    }
}
