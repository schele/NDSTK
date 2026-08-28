using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ScanResultTests
{
    private static CookieDeclarationCandidate Candidate(
        string name, CandidateFlag flag = CandidateFlag.None)
        => new(name, "Denna webbplats", "necessary", "Syfte.", "Session", "Cookie",
            flag, ConsentPass.Undecided, "https://ndstk.se/");

    private static ScanResult Result(
        bool canReachApi, bool writeBackSucceeded, bool withViolation)
        => new(
            Candidates: [Candidate("a")],
            Violations: withViolation ? [Candidate("_fbp", CandidateFlag.Violation)] : [],
            ExpectedButNotObserved: [],
            HostsByPass: new Dictionary<ConsentPass, IReadOnlyList<string>>(),
            Outcome: writeBackSucceeded ? new MergeOutcome([], [], [], Guid.Empty, true) : null,
            CanReachApi: canReachApi,
            DryRun: false,
            CompletedAt: new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            Site: "https://ndstk.se/");

    // Report-only is a supported mode, so having no credentials is not an error.
    [Fact]
    public void No_credentials_and_no_violations_is_clean()
    {
        Assert.Equal(0, Result(canReachApi: false, writeBackSucceeded: false, withViolation: false).ExitCode);
    }

    // A missing credential must never mask a violation - the whole point of the exit code is that
    // CI can gate on it.
    [Fact]
    public void A_violation_fails_the_run_even_with_no_credentials()
    {
        Assert.Equal(1, Result(canReachApi: false, writeBackSucceeded: false, withViolation: true).ExitCode);
    }

    [Fact]
    public void A_successful_write_back_with_no_violations_is_clean()
    {
        Assert.Equal(0, Result(canReachApi: true, writeBackSucceeded: true, withViolation: false).ExitCode);
    }

    // The case that matters: a write-back that was configured, attempted and failed. Returning 0
    // here would let a CI job stay green while the policy page silently stopped being updated.
    [Fact]
    public void A_configured_write_back_that_failed_is_an_error()
    {
        Assert.Equal(2, Result(canReachApi: true, writeBackSucceeded: false, withViolation: false).ExitCode);
    }

    // Violations outrank a failed write-back: the finding is more important than the plumbing.
    [Fact]
    public void A_violation_outranks_a_failed_write_back()
    {
        Assert.Equal(1, Result(canReachApi: true, writeBackSucceeded: false, withViolation: true).ExitCode);
    }

    // An empty scan never posts, so a null outcome there means "nothing to send", not "failed".
    [Fact]
    public void An_empty_scan_with_credentials_is_clean_rather_than_an_error()
    {
        var empty = new ScanResult(
            Candidates: [], Violations: [], ExpectedButNotObserved: [],
            HostsByPass: new Dictionary<ConsentPass, IReadOnlyList<string>>(),
            Outcome: null, CanReachApi: true, DryRun: false,
            CompletedAt: DateTimeOffset.UnixEpoch, Site: "https://ndstk.se/");

        Assert.Equal(0, empty.ExitCode);
    }
}
