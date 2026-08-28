# Cookie Scanner Desktop UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the finished cookie scanner a WinForms window that runs a scan, shows its findings, and lets any two past scans be compared — without the console tool losing anything CI depends on.

**Architecture:** The scan engine stops writing to `Console` and writes to an injected `IScanLog` instead. The orchestration currently inline in `Program.cs` becomes a reusable `ScanRunner` returning a `ScanResult`. Two executables then drive that one runner: the existing console tool (unchanged behaviour, `net10.0`, still CI-gateable) and a new WinForms window (`net10.0-windows`). Scan history is the report JSON itself, kept per run and made round-trippable.

**Tech Stack:** .NET 10, WinForms, Playwright 1.62.0, System.Text.Json, xUnit.

**Spec:** [docs/superpowers/specs/2026-08-28-cookie-scanner-ui-design.md](../specs/2026-08-28-cookie-scanner-ui-design.md)

## Global Constraints

- **.NET 10**, nullable reference types enabled, implicit usings enabled.
- **`NDSTK.CookieScanner` must NOT reference WinForms** and must stay `net10.0`. Only the new GUI project targets `net10.0-windows`. The dependency runs one way: the window references the scanner, never the reverse.
- **`NDSTK.CookieScan.Core` keeps zero dependencies** — no `PackageReference`, no `ProjectReference`. It must never reference the scanner project, which is why `ScanDiff` takes candidate lists rather than a `ScanResult`.
- **Message text does not change.** The refactor moves where a string goes, never what it says. This is what makes the baseline comparison in Task 9 meaningful.
- **The client secret and the member password are never persisted.** The secret comes only from `NDSTK_COOKIESCAN_CLIENT_SECRET`; the member password is typed per run into a masked field. The CLI deliberately refuses a `--client-secret` flag so a secret cannot reach shell history — a settings file storing one would undo that.
- **The exit-code rule is unchanged:** violations → 1; a write-back that was configured, attempted and failed → 2; otherwise 0. A missing credential is not an error, because report-only is a supported mode.
- **Visitor-facing copy is Swedish**; identifiers, comments, log messages and UI labels are English.
- **The user starts and stops the site.** No task starts or restarts it. The site holds a file lock on `NDSTK.dll`, so **never build `NDSTK.csproj` or `NDSTK.slnx`** unless the task says the site is down — build `NDSTK.CookieScan.Core`, `NDSTK.CookieScanner`, `NDSTK.CookieScanner.Gui` and `NDSTK.Tests` individually.
- **The user commits manually.** No task contains a `git commit` step; each ends with a verification checkpoint.
- **Site under test:** `https://localhost:44351`. Consent endpoint throttle is 10 posts per IP per minute; a full scan makes 6, a member scan 7 — leave a minute between runs.
- **Branch:** create `feature/cookie-scanner-ui` from `master` before Task 1.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `NDSTK.CookieScanner/IScanLog.cs` | The log abstraction, two levels |
| `NDSTK.CookieScanner/ConsoleScanLog.cs` | Info → stdout, Warning → stderr |
| `NDSTK.CookieScanner/ScanResult.cs` | Everything one scan produced, plus its exit code |
| `NDSTK.CookieScanner/ScanRunner.cs` | The orchestration, extracted from `Program.cs` |
| `NDSTK.CookieScanner/ScanHistory.cs` | Per-run JSON under `%LOCALAPPDATA%`, list/load/prune |
| `NDSTK.CookieScanner/Program.cs` | Thin CLI: parse, run, write files, print summary, exit |
| `NDSTK.CookieScan.Core/ScanDiff.cs` | Pure comparison of two candidate lists |
| `NDSTK.CookieScanner.Gui/*` | The window |
| `NDSTK.Tests/*Tests.cs` | `ScanDiff`, `ScanResult.ExitCode`, `ScanHistory`, `ConsoleScanLog` |

**Engine files that gain an `IScanLog`** and their current console-call counts:
`BrowserBootstrap` (1), `SiteCrawler` (1), `PageCapture` (2), `MemberDimension` (3),
`ManagementApiClient` (3). `ConsentPassRunner` has none of its own but must take one to
pass to `PageCapture`. `ScanReportWriter`'s 13 writes are the console summary and move to
the CLI wholesale rather than being converted.

---

## Task 1: `IScanLog` and the engine refactor

**Files:**
- Create: `NDSTK.CookieScanner/IScanLog.cs`, `NDSTK.CookieScanner/ConsoleScanLog.cs`
- Modify: `BrowserBootstrap.cs`, `SiteCrawler.cs`, `PageCapture.cs`, `ConsentPassRunner.cs`, `MemberDimension.cs`, `ManagementApiClient.cs`, `Program.cs`
- Test: `NDSTK.Tests/ConsoleScanLogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `interface IScanLog { void Info(string message); void Warning(string message); }` and `sealed class ConsoleScanLog : IScanLog`. Every later task takes an `IScanLog`.

- [ ] **Step 1: Write `IScanLog`**

```csharp
namespace NDSTK.CookieScanner;

/// <summary>Where the scan's running commentary goes.</summary>
/// <remarks>
/// Injected rather than written straight to the console, because the same scan drives a console
/// tool and a window. Two levels only: <see cref="Info"/> is progress a reader expects, and
/// <see cref="Warning"/> is something that went wrong without stopping the scan - a page that
/// would not load, a storage read that failed, a write-back that could not complete. The
/// distinction matters to the window, which colours them differently, and to the console, which
/// sends warnings to stderr so a pipeline can separate them.
/// </remarks>
public interface IScanLog
{
    void Info(string message);

    void Warning(string message);
}
```

- [ ] **Step 2: Write the failing test for `ConsoleScanLog`**

`NDSTK.Tests/ConsoleScanLogTests.cs`:

```csharp
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ConsoleScanLogTests
{
    // The console tool's contract with a pipeline: progress on stdout, problems on stderr, so a
    // caller can redirect one without losing the other. That split is the only reason this class
    // exists rather than the engine calling Console directly.
    [Fact]
    public void Info_goes_to_standard_output_and_warning_to_standard_error()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var log = new ConsoleScanLog();
            log.Info("progress");
            log.Warning("something went wrong");
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        Assert.Contains("progress", output.ToString());
        Assert.DoesNotContain("something went wrong", output.ToString());
        Assert.Contains("something went wrong", error.ToString());
        Assert.DoesNotContain("progress", error.ToString());
    }

    // The engine passes fully-formed sentences; the log must not decorate them with levels or
    // timestamps. The baseline comparison in the final task depends on the console output being
    // byte-identical to what the pre-refactor build produced.
    [Fact]
    public void Messages_are_written_verbatim_with_no_prefix()
    {
        var output = new StringWriter();
        TextWriter previousOut = Console.Out;

        try
        {
            Console.SetOut(output);
            new ConsoleScanLog().Info("  pass 1/6: Undecided");
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        Assert.Equal("  pass 1/6: Undecided", output.ToString().TrimEnd('\r', '\n'));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ConsoleScanLogTests`
Expected: build failure, `CS0246: The type or namespace name 'ConsoleScanLog' could not be found`.

- [ ] **Step 4: Write `ConsoleScanLog`**

`NDSTK.CookieScanner/ConsoleScanLog.cs`:

```csharp
namespace NDSTK.CookieScanner;

/// <summary>
/// The console tool's log: progress to stdout, problems to stderr.
/// </summary>
/// <remarks>
/// Writes the message verbatim, with no level prefix or timestamp. The engine passes complete
/// sentences and the pre-refactor build wrote exactly those strings, so decorating them here would
/// break the output comparison that gates this refactor - and would make a pipeline grepping the
/// output start missing lines.
/// </remarks>
public sealed class ConsoleScanLog : IScanLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warning(string message) => Console.Error.WriteLine(message);
}
```

- [ ] **Step 5: Run it to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ConsoleScanLogTests`
Expected: both tests pass.

- [ ] **Step 6: Thread the log through the engine**

Six files. **Change only how a message is emitted — never the message itself.** Every string stays byte-identical, including its leading spaces.

`BrowserBootstrap.cs` — the class is static; add an `IScanLog log` parameter to `EnsureChromium`:

```csharp
    public static void EnsureChromium(IScanLog log)
    {
        log.Info("Checking for a Chromium build...");
```

`SiteCrawler.cs` — currently `SiteCrawler(IPage page, ScanOptions options)`. Add the log:

```csharp
public sealed class SiteCrawler(IPage page, ScanOptions options, IScanLog log)
```

and its one `Console.Error.WriteLine($"  skipped {current} ...")` becomes `log.Warning(...)` with the identical interpolated string.

`PageCapture.cs` — static; both `VisitAsync` and `KeysAsync` gain an `IScanLog log` parameter, and `VisitAsync` passes it down to `KeysAsync`. `RecordHosts` writes nothing and does not need one. Both `Console.Error.WriteLine` calls become `log.Warning`.

`ConsentPassRunner.cs` — writes nothing itself but calls `PageCapture.VisitAsync`, so it needs a log to pass on:

```csharp
public sealed class ConsentPassRunner(IBrowser browser, ScanOptions options, string endpointPath, IScanLog log)
```

`MemberDimension.cs` — same constructor addition; its three `Console.Error.WriteLine` calls become `log.Warning`; it passes the log to its `SiteCrawler` and `PageCapture` calls.

`ManagementApiClient.cs` — currently `ManagementApiClient(ScanOptions options)`. Becomes `ManagementApiClient(ScanOptions options, IScanLog log)`; its three `Console.Error.WriteLine` calls become `log.Warning`.

`Program.cs` — construct one `ConsoleScanLog` near the top and pass it to each of the above. Leave `Program.cs`'s own eight console writes alone for now; Task 3 restructures this file.

- [ ] **Step 7: Verify no console writes remain in the engine**

Run: `grep -n "Console\." NDSTK.CookieScanner/BrowserBootstrap.cs NDSTK.CookieScanner/SiteCrawler.cs NDSTK.CookieScanner/PageCapture.cs NDSTK.CookieScanner/ConsentPassRunner.cs NDSTK.CookieScanner/MemberDimension.cs NDSTK.CookieScanner/ManagementApiClient.cs`

Expected: **no matches.** Any match is a call that was missed, and the window would silently lose that message.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` — build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — all tests pass (190 + 2 new = 192).

**Then run the live comparison, which is the point of this task:**

```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 \
  --client-id cookie-scanner --dry-run --report-dir ./scan-out > /tmp/t1-out.txt 2> /tmp/t1-err.txt
diff /tmp/t1-out.txt .superpowers/sdd/ui-baseline/public-stdout.txt
diff /tmp/t1-err.txt .superpowers/sdd/ui-baseline/public-stderr.txt
```

The baseline files carry an `exit=N` line appended after the output; expect `diff` to report only that line as missing. **Any other difference is a regression** — a message that changed wording, moved stream, or vanished. Investigate rather than updating the baseline.

Also compare the warning paths, which the happy path never exercises:

```bash
dotnet run --project NDSTK.CookieScanner -- --url http://localhost:59999 --max-pages 3 \
  > /tmp/t1-refused-out.txt 2> /tmp/t1-refused-err.txt
diff /tmp/t1-refused-err.txt .superpowers/sdd/ui-baseline/refused-stderr.txt
```

Expected: identical. This is the only check covering `SiteCrawler`'s skip warning.

Run: `rm -rf ./scan-out` and `git status --short` — expect the nine files of this task.

---

## Task 2: `ScanResult` and the exit-code rule

**Files:**
- Create: `NDSTK.CookieScanner/ScanResult.cs`
- Test: `NDSTK.Tests/ScanResultTests.cs`

**Interfaces:**
- Consumes: `CookieDeclarationCandidate`, `ConsentPass` from Core; `MergeOutcome` from `NDSTK.CookieScanner`.
- Produces: `sealed record ScanResult(...)` with `int ExitCode`. Tasks 3–9 all consume it.

The exit-code rule currently lives inside `ScanReportWriter.Write`, a method that also writes two files and prints a summary — which is why the cookie scanner's whole-branch review recorded it as untestable. Moving it here closes that gap.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/ScanResultTests.cs`:

```csharp
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
            HostsByPass: new Dictionary<ConsentPass, IReadOnlySet<string>>(),
            Outcome: writeBackSucceeded ? new MergeOutcome([], [], [], true) : null,
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
            HostsByPass: new Dictionary<ConsentPass, IReadOnlySet<string>>(),
            Outcome: null, CanReachApi: true, DryRun: false,
            CompletedAt: DateTimeOffset.UnixEpoch, Site: "https://ndstk.se/");

        Assert.Equal(0, empty.ExitCode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanResultTests`
Expected: build failure, `CS0246: The type or namespace name 'ScanResult' could not be found`.

- [ ] **Step 3: Write `ScanResult`**

`NDSTK.CookieScanner/ScanResult.cs`:

```csharp
using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Everything one scan produced. Serialized as-is to both the report file and the history
/// folder, so it must stay round-trippable: no computed collections, no types System.Text.Json
/// cannot rebuild.
/// </summary>
public sealed record ScanResult(
    IReadOnlyList<CookieDeclarationCandidate> Candidates,
    IReadOnlyList<CookieDeclarationCandidate> Violations,
    IReadOnlyList<string> ExpectedButNotObserved,
    IReadOnlyDictionary<ConsentPass, IReadOnlySet<string>> HostsByPass,
    MergeOutcome? Outcome,
    bool CanReachApi,
    bool DryRun,
    DateTimeOffset CompletedAt,
    string Site)
{
    /// <summary>
    /// The process exit code. Findings outrank plumbing, and configuration is never an error on
    /// its own.
    /// </summary>
    /// <remarks>
    /// A missing credential returns 0 because report-only is a supported mode. A write-back that
    /// was configured, attempted and failed returns 2, because a CI job gating on this would
    /// otherwise stay green while the policy page silently stopped being updated. An empty scan
    /// never posts at all, so a null outcome there means "nothing to send" rather than "failed".
    /// </remarks>
    [JsonIgnore]
    public int ExitCode =>
        Violations.Count > 0 ? 1
        : Outcome is null && CanReachApi && Candidates.Count > 0 ? 2
        : 0;

    /// <summary>Candidates the scan could not attribute to a single category.</summary>
    /// <remarks>
    /// Derived rather than stored, so the serialized form has one source of truth for a
    /// candidate's flag.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<CookieDeclarationCandidate> NeedsReview =>
        [.. Candidates.Where(candidate => candidate.Flag == CandidateFlag.NeedsReview)];
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanResultTests`
Expected: all six pass.

- [ ] **Step 5: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` — succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 198 passing.
Run: `git status --short` — the two files of this task.

---

## Task 3: `ScanRunner`, and the CLI becomes thin

**Files:**
- Create: `NDSTK.CookieScanner/ScanRunner.cs`
- Modify: `NDSTK.CookieScanner/Program.cs`, `NDSTK.CookieScanner/ScanReportWriter.cs`

**Interfaces:**
- Consumes: `IScanLog` (Task 1), `ScanResult` (Task 2), and every engine class.
- Produces: `sealed class ScanRunner(ScanOptions options, CookieCatalogue catalogue, IScanLog log)` with `Task<ScanResult?> RunAsync(CancellationToken cancellationToken)`, returning null when discovery found no pages. `ScanReportWriter.WriteFiles(ScanOptions, ScanResult)` and `ScanReportWriter.SummaryLines(ScanResult)`. Tasks 5–9 consume all of these.

- [ ] **Step 1: Write `ScanRunner`**

Move the body of `Program.cs`'s `try` block into `RunAsync`, changing only what the spec requires: it takes a `CancellationToken`, it logs through `log` rather than `Console`, and it returns a `ScanResult` instead of calling the report writer.

`NDSTK.CookieScanner/ScanRunner.cs`:

```csharp
using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Runs one scan and returns what it found. Drives both front ends, so neither can drift from
/// what the other does - the console tool's exit code is what gates CI, and a window showing
/// different findings than CI acts on would be worse than no window.
/// </summary>
public sealed class ScanRunner(ScanOptions options, CookieCatalogue catalogue, IScanLog log)
{
    // The package's default consent endpoint. Not a flag: a site that has moved it has also moved
    // its own JavaScript, so a mismatch here would be the least of that site's problems.
    private const string ConsentEndpointPath = "/api/cookie-consent";

    /// <summary>
    /// Returns null when discovery found no pages - there is nothing to report, and reporting an
    /// empty scan as a successful one would be a lie about coverage.
    /// </summary>
    public async Task<ScanResult?> RunAsync(CancellationToken cancellationToken)
    {
        log.Info($"Scanning {options.Url} - up to {options.MaxPages} pages per pass, locale {options.Locale}.");

        BrowserBootstrap.EnsureChromium(log);

        using IPlaywright playwright = await Playwright.CreateAsync();

        await using IBrowser browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = options.Headed is false });

        IReadOnlyList<Uri> urls;

        // Discovery runs in its own throwaway context so the pages it loads cannot leave cookies
        // in any pass's jar.
        //
        // IgnoreHTTPSErrors is scoped to a loopback target, same as ManagementApiClient: it exists
        // so a local site behind a dev certificate can be scanned without trusting that
        // certificate first, and is deliberately not extended to a real host. MemberDimension
        // submits a member's email and password through one of these contexts.
        await using (IBrowserContext discovery = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = options.Url.IsLoopback }))
        {
            urls = await new SiteCrawler(await discovery.NewPageAsync(), options, log)
                .DiscoverAsync(options.Url);
        }

        if (urls.Count == 0)
        {
            log.Warning($"Found no HTML pages at {options.Url}. Is the site running, and is the URL right?");

            return null;
        }

        log.Info($"Discovered {urls.Count} page(s). Running {ConsentPasses.Comparable.Count} passes.");

        var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath, log);
        List<ObservedEntry> observed = [];
        Dictionary<ConsentPass, IReadOnlySet<string>> hostsByPass = [];

        foreach (ConsentPass pass in ConsentPasses.Comparable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            log.Info($"  pass {(int)pass + 1}/{ConsentPasses.Comparable.Count}: {pass}");

            PassResult result = await runner.RunAsync(pass, urls);

            hostsByPass[pass] = result.Hosts;
            observed.AddRange(result.Entries.Select(entry => new ObservedEntry(
                entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires)));
        }

        if (options.MemberScanEnabled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            log.Info("  member dimension: signing in");

            PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath, log)
                .RunAsync(urls);

            hostsByPass[ConsentPass.MemberArea] = member.Hosts;
            observed.AddRange(member.Entries.Select(entry => new ObservedEntry(
                entry.Name, entry.Storage, ConsentPass.MemberArea,
                entry.FirstUrl.ToString(), entry.Expires)));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<CookieDeclarationCandidate> candidates = ObservedEntries
            .EarliestPerName(observed)
            .Select(entry => CategoryInference.Classify(entry, catalogue, now, options.Locale))
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.FirstSeenPass).First())
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

        // From the RAW observations, not from candidates. A violation is a property of one
        // sighting, while a candidate is the earliest-per-name reduction - so deriving violations
        // from the reduced list would miss a cookie whose category WAS granted in the pass that
        // first set it and which was then set again in a pass that granted something else.
        IReadOnlyList<CookieDeclarationCandidate> violations =
            ViolationScan.Find(observed, catalogue, now, options.Locale);

        // Computed here rather than taken from the endpoint: it depends on THIS run's catalogue,
        // which may be an override file the site knows nothing about.
        IReadOnlyList<string> expectedButNotObserved =
            [.. MergePlanner.Plan(candidates, [], catalogue).ExpectedButNotObserved];

        MergeOutcome? outcome = null;

        // An empty scan is a legitimate outcome, not a failure - posting nothing would earn a 400.
        if (options.CanReachApi && candidates.Count > 0)
        {
            outcome = await new ManagementApiClient(options, log).MergeAsync(candidates);
        }

        return new ScanResult(
            candidates, violations, expectedButNotObserved, hostsByPass, outcome,
            options.CanReachApi, options.DryRun, now, options.Url.ToString());
    }
}
```

- [ ] **Step 2: Split `ScanReportWriter`**

`Write` currently writes two files, prints 13 console lines, and returns an exit code. Split it:

- `public static void WriteFiles(ScanOptions options, ScanResult result)` — creates the report directory and writes `cookie-scan-report.md` and `cookie-scan-report.json`. Keep the markdown generation exactly as it is, sourcing its sections from `result` rather than from separate parameters. `result.NeedsReview` replaces the local filtering.
- `public static IReadOnlyList<string> SummaryLines(ScanResult result)` — returns the lines the console summary used to print, in the same order and wording, instead of printing them.
- Delete the exit-code logic; it now lives on `ScanResult`.

Keeping the summary as *lines returned* rather than *lines printed* is what lets the CLI print them and the window ignore them — the window shows its findings in a grid and would otherwise get a console-formatted duplicate.

- [ ] **Step 3: Rewrite `Program.cs` as a thin CLI**

```csharp
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

try
{
    ScanOptions options = ScanOptions.Parse(args);
    var log = new ConsoleScanLog();

    ScanResult? result = await new ScanRunner(options, LoadCatalogue(log), log)
        .RunAsync(CancellationToken.None);

    if (result is null)
    {
        return ScanReportWriter.ExitError;
    }

    ScanReportWriter.WriteFiles(options, result);
    ScanHistory.Save(result);

    foreach (string line in ScanReportWriter.SummaryLines(result))
    {
        log.Info(line);
    }

    return result.ExitCode;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);

    return ScanReportWriter.ExitError;
}
catch (Exception error)
{
    Console.Error.WriteLine($"The scan failed: {error.Message}");

    return ScanReportWriter.ExitError;
}

// An override beside the exe replaces the embedded catalogue wholesale, so legal wording can be
// changed without a rebuild.
static CookieCatalogue LoadCatalogue(IScanLog log)
{
    string beside = Path.Combine(AppContext.BaseDirectory, "cookie-catalogue.json");

    if (File.Exists(beside))
    {
        log.Info($"Using the catalogue override at {beside}.");

        return CookieCatalogue.Parse(File.ReadAllText(beside));
    }

    return CookieCatalogue.Default();
}
```

`ScanHistory.Save` arrives in Task 5. **Until then, comment that one line out** with a `TASK 5 RESTORES THIS` marker, exactly as the cookie scanner's own plan did for its API client, so the project keeps building and the comparison in Step 4 can run.

The two outer `Console.Error.WriteLine` calls stay as `Console` rather than becoming `log.Warning`: they run when `Parse` threw, before a log could sensibly exist, and when the log itself may be the thing that failed.

- [ ] **Step 4: Verification checkpoint — the comparison again**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` — succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 198 passing.

Then repeat Task 1 Step 8's four diffs — public, refused, badcreds, and the member scan:

```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 \
  --client-id cookie-scanner --dry-run --member-email 'cookie-scan-test@ndstk.local' \
  --member-password 'amRTkMr4GULF0h9Aa!' --report-dir ./scan-out \
  > /tmp/t3-member-out.txt 2> /tmp/t3-member-err.txt
diff /tmp/t3-member-out.txt .superpowers/sdd/ui-baseline/member-stdout.txt
```

Expected: only the appended `exit=0` line differs. **This is the task most likely to reorder or drop a message**, because it moves every one of them. Any other difference is a regression.

Run: `rm -rf ./scan-out`; `git status --short` — the three files of this task.

---

## Task 4: A round-trippable report

**Files:**
- Modify: `NDSTK.CookieScanner/ScanReportWriter.cs`
- Create: `NDSTK.CookieScanner/ScanJson.cs`
- Test: `NDSTK.Tests/ScanJsonTests.cs`

**Interfaces:**
- Consumes: `ScanResult` (Task 2).
- Produces: `static class ScanJson` with `JsonSerializerOptions Options`, `string Serialize(ScanResult)`, `ScanResult? Deserialize(string json)`. Tasks 5 and 8 consume it.

Today's `cookie-scan-report.json` is an anonymous object assembled inline — readable, and impossible to load back. History has to load a past scan into the same grid a live one uses, so the file must round-trip.

- [ ] **Step 1: Write the failing test**

`NDSTK.Tests/ScanJsonTests.cs`:

```csharp
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ScanJsonTests
{
    private static ScanResult Sample() => new(
        Candidates:
        [
            new("_ga_*", "Google Analytics", "statistics", "Mäter.", "24 månader", "Cookie",
                CandidateFlag.NeedsReview, ConsentPass.AcceptAll, "https://ndstk.se/"),
        ],
        Violations:
        [
            new("_fbp", "Meta", "marketing", "Annonser.", "3 månader", "Cookie",
                CandidateFlag.Violation, ConsentPass.RejectAll, "https://ndstk.se/x"),
        ],
        ExpectedButNotObserved: ["UMB_MEMBER"],
        HostsByPass: new Dictionary<ConsentPass, IReadOnlySet<string>>
        {
            [ConsentPass.AcceptAll] = new HashSet<string> { "www.google-analytics.com" },
        },
        Outcome: new MergeOutcome(["_ga_*"], ["ndstk-consent"], ["old"], true),
        CanReachApi: true,
        DryRun: false,
        CompletedAt: new DateTimeOffset(2026, 8, 28, 9, 30, 0, TimeSpan.Zero),
        Site: "https://ndstk.se/");

    // The history browser loads past scans back into the same grid a live scan fills, so a report
    // that cannot be read back is a report history cannot use.
    [Fact]
    public void A_result_survives_a_round_trip_intact()
    {
        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(Sample()));

        Assert.NotNull(back);
        Assert.Equal("https://ndstk.se/", back.Site);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 9, 30, 0, TimeSpan.Zero), back.CompletedAt);
        Assert.Single(back.Candidates);
        Assert.Equal("_ga_*", back.Candidates[0].Name);
        Assert.Equal("Mäter.", back.Candidates[0].Purpose);
        Assert.Equal(CandidateFlag.NeedsReview, back.Candidates[0].Flag);
        Assert.Single(back.Violations);
        Assert.Equal(ConsentPass.RejectAll, back.Violations[0].FirstSeenPass);
        Assert.Equal(["UMB_MEMBER"], back.ExpectedButNotObserved);
        Assert.True(back.Outcome!.Saved);
        Assert.Equal(["_ga_*"], back.Outcome.Added);
        Assert.True(back.CanReachApi);
    }

    // The hosts dictionary is keyed by an enum. Without a converter it serialises as an integer
    // key on one side and a name on the other, which is how the pre-UI report ended up encoding
    // ConsentPass two different ways in the same file.
    [Fact]
    public void The_hosts_dictionary_round_trips_with_its_enum_key()
    {
        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(Sample()));

        Assert.True(back!.HostsByPass.ContainsKey(ConsentPass.AcceptAll));
        Assert.Contains("www.google-analytics.com", back.HostsByPass[ConsentPass.AcceptAll]);
    }

    // Enums are written as names, not integers, so the file is readable by a human and stable if
    // an enum member is ever reordered.
    [Fact]
    public void Enums_are_written_as_names()
    {
        string json = ScanJson.Serialize(Sample());

        Assert.Contains("RejectAll", json);
        Assert.DoesNotContain("\"firstSeenPass\": 1", json);
    }

    // History skips a file it cannot parse rather than failing the whole list, so Deserialize must
    // return null instead of throwing.
    [Fact]
    public void Unparseable_json_returns_null_rather_than_throwing()
    {
        Assert.Null(ScanJson.Deserialize("this is not json"));
        Assert.Null(ScanJson.Deserialize("[]"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanJsonTests`
Expected: `CS0246: The type or namespace name 'ScanJson' could not be found`.

- [ ] **Step 3: Write `ScanJson`**

`NDSTK.CookieScanner/ScanJson.cs`:

```csharp
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
```

- [ ] **Step 4: Point the report writer at it**

In `ScanReportWriter.WriteFiles`, replace the inline anonymous-object serialization with
`ScanJson.Serialize(result)`. The markdown file is unchanged.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanJsonTests`
Expected: all four pass.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 202 passing.

Run a live scan and confirm the new JSON round-trips and the markdown is unchanged:

```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 \
  --report-dir ./scan-out
diff ./scan-out/cookie-scan-report.md .superpowers/sdd/ui-baseline/cookie-scan-report.md
```

Expected: the markdown diff is empty. The JSON will differ from the baseline copy — that is this task's intended change. Confirm by eye that the new JSON has camelCase keys, `"firstSeenPass": "Undecided"` as a name, and that `hostsByPass` keys are pass names.

Run: `rm -rf ./scan-out`; `git status --short` — the four files of this task.


---

## Task 5: `ScanHistory`

**Files:**
- Create: `NDSTK.CookieScanner/ScanHistory.cs`
- Modify: `NDSTK.CookieScanner/Program.cs` — restore the commented `ScanHistory.Save` line
- Test: `NDSTK.Tests/ScanHistoryTests.cs`

**Interfaces:**
- Consumes: `ScanResult` (Task 2), `ScanJson` (Task 4).
- Produces: `sealed class ScanHistory` with `ScanHistory(string folder)`, `static ScanHistory Default()`, `void Save(ScanResult)`, `IReadOnlyList<ScanHistoryEntry> List()`, `ScanResult? Load(ScanHistoryEntry)`, and `static void Save(ScanResult)` as a convenience over `Default()`. `sealed record ScanHistoryEntry(string Path, DateTimeOffset CompletedAt, string Site, int EntryCount, int ExitCode)`. Task 8 consumes all of it.

The folder is injectable so the tests use a temporary directory rather than the real
`%LOCALAPPDATA%` — a test that writes to a developer's actual history folder is a test that
pollutes what it is meant to verify.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/ScanHistoryTests.cs`:

```csharp
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
            HostsByPass: new Dictionary<ConsentPass, IReadOnlySet<string>>(),
            Outcome: null,
            CanReachApi: false,
            DryRun: false,
            CompletedAt: completedAt,
            Site: "https://ndstk.se/");

    [Fact]
    public void A_saved_scan_can_be_listed_and_loaded_back()
    {
        var history = new ScanHistory(folder);
        history.Save(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), candidates: 3));

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
        history.Save(Result(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero)));
        history.Save(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));
        history.Save(Result(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero)));

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

        for (int day = 1; day <= 55; day++)
        {
            history.Save(Result(new DateTimeOffset(2026, 1, day, 10, 0, 0, TimeSpan.Zero)));
        }

        IReadOnlyList<ScanHistoryEntry> entries = history.List();

        Assert.Equal(50, entries.Count);
        // The five oldest went, not the five newest.
        Assert.Equal(55, entries[0].CompletedAt.Day);
        Assert.Equal(6, entries[^1].CompletedAt.Day);
    }

    // The folder holds files this code did not necessarily write. One unreadable file must cost
    // its own row, not the whole list - a history browser that throws on startup is useless.
    [Fact]
    public void An_unparseable_file_is_skipped_rather_than_failing_the_list()
    {
        var history = new ScanHistory(folder);
        history.Save(Result(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)));
        File.WriteAllText(Path.Combine(folder, "20260101-000000-junk.json"), "not json at all");

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

        history.Save(Result(instant));
        history.Save(Result(instant));

        Assert.Equal(2, history.List().Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanHistoryTests`
Expected: `CS0246: The type or namespace name 'ScanHistory' could not be found`.

- [ ] **Step 3: Write `ScanHistory`**

`NDSTK.CookieScanner/ScanHistory.cs`:

```csharp
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
        }
    }
}
```

- [ ] **Step 4: Restore the call in `Program.cs`**

Replace the `TASK 5 RESTORES THIS` marker and its commented line with the live call
`ScanHistory.Save(result);`, placed immediately after `ScanReportWriter.WriteFiles(...)`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanHistoryTests`
Expected: all six pass.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` — succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 208 passing.

Then a live run, and confirm it landed in the real folder:

```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 --report-dir ./scan-out
ls "$LOCALAPPDATA/NDSTK.CookieScanner/scans/"
```

Expected: one `.json` file named with today's UTC date. Run: `rm -rf ./scan-out`.

Run: `git status --short` — the three files of this task.

---

## Task 6: `ScanDiff`

**Files:**
- Create: `NDSTK.CookieScan.Core/ScanDiff.cs`
- Test: `NDSTK.Tests/ScanDiffTests.cs`

**Interfaces:**
- Consumes: `CookieDeclarationCandidate` from Core.
- Produces: `sealed record CategoryChange(string Name, string From, string To)` and `sealed record ScanDiff(IReadOnlyList<CookieDeclarationCandidate> Appeared, IReadOnlyList<CookieDeclarationCandidate> Disappeared, IReadOnlyList<CategoryChange> Recategorised)` with `static ScanDiff Between(IReadOnlyList<CookieDeclarationCandidate> older, IReadOnlyList<CookieDeclarationCandidate> newer)`. Task 8 consumes it.

It takes candidate lists rather than two `ScanResult`s deliberately: `ScanResult` lives in
`NDSTK.CookieScanner`, and Core must not reference the scanner — that one-way dependency is
what keeps Core free of Playwright, HTTP and Umbraco. The caller passes
`older.Candidates` and `newer.Candidates`, which is all a diff needs.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/ScanDiffTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class ScanDiffTests
{
    private static CookieDeclarationCandidate Candidate(string name, string category = "necessary")
        => new(name, "Denna webbplats", category, "Syfte.", "Session", "Cookie",
            CandidateFlag.None, ConsentPass.Undecided, "https://ndstk.se/");

    // The question the whole history feature exists to answer: what turned up after that deploy?
    [Fact]
    public void A_cookie_only_in_the_newer_scan_appeared()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a")], [Candidate("a"), Candidate("_ga_*")]);

        Assert.Single(diff.Appeared);
        Assert.Equal("_ga_*", diff.Appeared[0].Name);
        Assert.Empty(diff.Disappeared);
        Assert.Empty(diff.Recategorised);
    }

    [Fact]
    public void A_cookie_only_in_the_older_scan_disappeared()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a"), Candidate("old")], [Candidate("a")]);

        Assert.Single(diff.Disappeared);
        Assert.Equal("old", diff.Disappeared[0].Name);
        Assert.Empty(diff.Appeared);
    }

    // A cookie changing category between runs means the site changed what it does with it, which
    // is a more interesting finding than either list.
    [Fact]
    public void A_cookie_whose_category_changed_is_reported_with_both_categories()
    {
        ScanDiff diff = ScanDiff.Between(
            [Candidate("x", "necessary")], [Candidate("x", "marketing")]);

        Assert.Single(diff.Recategorised);
        Assert.Equal("x", diff.Recategorised[0].Name);
        Assert.Equal("necessary", diff.Recategorised[0].From);
        Assert.Equal("marketing", diff.Recategorised[0].To);
        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
    }

    [Fact]
    public void Two_identical_scans_produce_three_empty_lists()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("a"), Candidate("b")], [Candidate("b"), Candidate("a")]);

        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
        Assert.Empty(diff.Recategorised);
    }

    [Fact]
    public void Everything_appeared_when_the_older_scan_is_empty()
    {
        ScanDiff diff = ScanDiff.Between([], [Candidate("a"), Candidate("b")]);

        Assert.Equal(2, diff.Appeared.Count);
        Assert.Empty(diff.Disappeared);
    }

    [Fact]
    public void Matching_names_ignores_case_like_the_rest_of_the_codebase()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("UMB_MEMBER")], [Candidate("umb_member")]);

        Assert.Empty(diff.Appeared);
        Assert.Empty(diff.Disappeared);
    }

    // Deliberately NOT glob matching. Two scans of the same site draw names from the same
    // catalogue, so a pattern in one is a pattern in the other; treating them as globs would
    // report a pattern and a literal that happen to overlap as unchanged when one genuinely
    // replaced the other.
    [Fact]
    public void A_pattern_and_a_name_it_would_match_are_treated_as_different_cookies()
    {
        ScanDiff diff = ScanDiff.Between([Candidate("_ga_*")], [Candidate("_ga_ABC123")]);

        Assert.Single(diff.Appeared);
        Assert.Single(diff.Disappeared);
    }

    [Fact]
    public void All_three_lists_are_ordered_by_name()
    {
        ScanDiff diff = ScanDiff.Between([], [Candidate("zebra"), Candidate("alpha"), Candidate("mid")]);

        Assert.Equal(["alpha", "mid", "zebra"], diff.Appeared.Select(candidate => candidate.Name));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanDiffTests`
Expected: `CS0246: The type or namespace name 'ScanDiff' could not be found`.

- [ ] **Step 3: Write `ScanDiff`**

`NDSTK.CookieScan.Core/ScanDiff.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>A cookie that was declared under one category and is now under another.</summary>
public sealed record CategoryChange(string Name, string From, string To);

/// <summary>
/// What changed between two scans of the same site.
/// </summary>
/// <remarks>
/// Matched by name, case-insensitively, and deliberately NOT as globs. Two scans of one site draw
/// their names from the same catalogue, so a pattern in one run is a pattern in the other; glob
/// matching would report a pattern and a literal that happen to overlap as unchanged, hiding the
/// case where one genuinely replaced the other.
/// </remarks>
public sealed record ScanDiff(
    IReadOnlyList<CookieDeclarationCandidate> Appeared,
    IReadOnlyList<CookieDeclarationCandidate> Disappeared,
    IReadOnlyList<CategoryChange> Recategorised)
{
    public static ScanDiff Between(
        IReadOnlyList<CookieDeclarationCandidate> older,
        IReadOnlyList<CookieDeclarationCandidate> newer)
    {
        Dictionary<string, CookieDeclarationCandidate> before =
            Index(older);
        Dictionary<string, CookieDeclarationCandidate> after =
            Index(newer);

        List<CookieDeclarationCandidate> appeared =
            [.. after.Where(entry => before.ContainsKey(entry.Key) is false).Select(entry => entry.Value)];

        List<CookieDeclarationCandidate> disappeared =
            [.. before.Where(entry => after.ContainsKey(entry.Key) is false).Select(entry => entry.Value)];

        List<CategoryChange> recategorised = [];

        foreach ((string key, CookieDeclarationCandidate was) in before)
        {
            if (after.TryGetValue(key, out CookieDeclarationCandidate? now)
                && string.Equals(was.Category, now.Category, StringComparison.Ordinal) is false)
            {
                recategorised.Add(new CategoryChange(now.Name, was.Category, now.Category));
            }
        }

        return new ScanDiff(
            [.. appeared.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)],
            [.. disappeared.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)],
            [.. recategorised.OrderBy(change => change.Name, StringComparer.Ordinal)]);
    }

    // Last one wins on a duplicate name, which cannot happen from a real scan - the runner already
    // reduces to one candidate per name - but a hand-edited history file should not throw.
    private static Dictionary<string, CookieDeclarationCandidate> Index(
        IReadOnlyList<CookieDeclarationCandidate> candidates)
    {
        Dictionary<string, CookieDeclarationCandidate> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (CookieDeclarationCandidate candidate in candidates)
        {
            index[candidate.Name] = candidate;
        }

        return index;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ScanDiffTests`
Expected: all eight pass.

- [ ] **Step 5: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj` — succeeded, and confirm the csproj still has no `PackageReference` or `ProjectReference`.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 216 passing.
Run: `git status --short` — the two files of this task.

---

## Task 7: The window, and the Scan tab

**Files:**
- Create: `NDSTK.CookieScanner.Gui/NDSTK.CookieScanner.Gui.csproj`, `Program.cs`, `MainForm.cs`, `MainForm.Scan.cs`, `TextBoxScanLog.cs`, `GuiSettings.cs`
- Modify: `NDSTK.slnx`, `NDSTK.csproj` (`DefaultItemExcludes`)

**Interfaces:**
- Consumes: `ScanOptions`, `ScanRunner`, `ScanResult`, `IScanLog`, `ScanReportWriter`, `ScanHistory` from `NDSTK.CookieScanner`; `CookieCatalogue` from Core.
- Produces: `MainForm`, and `sealed class TextBoxScanLog : IScanLog`. Task 8 adds a tab to the same form.

**Forms are built in code, not with the designer.** No `.Designer.cs`, no `.resx`. A
designer file is generated, awkward to review in a diff, and this form is simple enough
that hand-written layout is clearer than a generated one.

- [ ] **Step 1: Create the project**

`NDSTK.CookieScanner.Gui/NDSTK.CookieScanner.Gui.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>NDSTK.CookieScanner.Gui</RootNamespace>
    <AssemblyName>ndstk-cookiescan-ui</AssemblyName>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <!--
      Same two properties the console tool needs, and for the same reason: Playwright's driver
      ships node.exe, which the SDK classifies as content rather than a native library. With only
      the native-library switch the exe runs from its own output directory and then fails with
      "missing required assets" the moment it is copied anywhere - which is the only situation a
      portable exe exists for.
    -->
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\NDSTK.CookieScanner\NDSTK.CookieScanner.csproj" />
  </ItemGroup>

</Project>
```

Add it to `NDSTK.slnx` after `NDSTK.CookieScanner`, and **add `NDSTK.CookieScanner.Gui\**` to
`DefaultItemExcludes` in `NDSTK.csproj`** — the web project sits at the repository root, so
without that its default globs compile this project's sources into the web assembly and the
build fails with duplicate assembly attributes, naming neither project.

- [ ] **Step 2: Write `TextBoxScanLog`**

`NDSTK.CookieScanner.Gui/TextBoxScanLog.cs`:

```csharp
using NDSTK.CookieScanner;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// Appends the scan's commentary to a text box, colouring warnings.
/// </summary>
/// <remarks>
/// Every write marshals to the UI thread. The scan runs on a background task and the engine logs
/// from Playwright's own threads, so appending directly would throw an invalid-cross-thread
/// exception - and would do it on a failure path that is rarely exercised, which is the worst
/// place to discover it.
/// </remarks>
public sealed class TextBoxScanLog(RichTextBox target) : IScanLog
{
    public void Info(string message) => Append(message, target.ForeColor);

    public void Warning(string message) => Append(message, Color.Firebrick);

    private void Append(string message, Color colour)
    {
        if (target.IsHandleCreated is false || target.IsDisposed)
        {
            return;
        }

        if (target.InvokeRequired)
        {
            target.BeginInvoke(() => Append(message, colour));

            return;
        }

        target.SelectionStart = target.TextLength;
        target.SelectionLength = 0;
        target.SelectionColor = colour;
        target.AppendText(message + Environment.NewLine);
        target.SelectionColor = target.ForeColor;
        target.ScrollToCaret();
    }
}
```

- [ ] **Step 3: Write `GuiSettings`**

`NDSTK.CookieScanner.Gui/GuiSettings.cs`:

```csharp
using System.Text.Json;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// What the window remembers between runs.
/// </summary>
/// <remarks>
/// The client secret and the member password are deliberately absent and must stay absent. The
/// console tool refuses a --client-secret flag so a secret cannot reach shell history; a settings
/// file storing one would undo that to save a paste. The secret comes from
/// NDSTK_COOKIESCAN_CLIENT_SECRET and the member password is typed per run.
/// </remarks>
public sealed record GuiSettings(
    string Url = "https://localhost:44351",
    int MaxPages = 25,
    Locale Locale = Locale.Sv,
    string MemberEmail = "",
    string ClientId = "",
    bool DryRun = true)
{
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "settings.json");

    public static GuiSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(Path)) ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings are not worth refusing to start over.
            return new GuiSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Losing the remembered settings is a nuisance, not a reason to fail after a scan.
        }
    }
}
```

`DryRun` defaults to `true`: the window's first run should not write to a live policy page
because someone pressed the obvious button.

- [ ] **Step 4: Write `Program.cs` and the form shell**

`NDSTK.CookieScanner.Gui/Program.cs`:

```csharp
namespace NDSTK.CookieScanner.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

`NDSTK.CookieScanner.Gui/MainForm.cs` holds the shell: a `TabControl` with a "Scan" page and
a "History" page (Task 8 fills the second), sized 1000×700, titled "NDSTK cookie scanner",
with `MinimumSize` set so the grid cannot be crushed. Construct the controls in the
constructor and assign them to fields; put the Scan tab's controls and behaviour in
`MainForm.Scan.cs` as a partial class, so neither file grows unwieldy.

- [ ] **Step 5: Build the Scan tab**

`NDSTK.CookieScanner.Gui/MainForm.Scan.cs`, as `partial class MainForm`. Layout, top to
bottom: a `TableLayoutPanel` of labelled inputs — Site URL, Max pages (`NumericUpDown`,
1–500), Locale (`ComboBox` bound to `Locale` values), Member email, Member password
(`UseSystemPasswordChar = true`), API client id — then a "Dry run (write nothing)"
`CheckBox`, then a Run and a Cancel button, then a `SplitContainer` with the
`RichTextBox` log above and a `DataGridView` of findings below.

Beside the client-id field, a label reading either `NDSTK_COOKIESCAN_CLIENT_SECRET is set`
or, when the variable is absent, `NDSTK_COOKIESCAN_CLIENT_SECRET is not set - write-back
will be skipped`. Read it once at construction. Telling the operator up front beats letting
the run fail at the token request.

The Run handler:

```csharp
    private async void OnRunClicked(object? sender, EventArgs e)
    {
        ScanOptions options;

        try
        {
            options = BuildOptions();
        }
        catch (ArgumentException error)
        {
            MessageBox.Show(this, error.Message, "Cannot start", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        SetRunning(true);
        log.Clear();
        findings.Rows.Clear();
        cancellation = new CancellationTokenSource();

        var scanLog = new TextBoxScanLog(log);

        try
        {
            // Task.Run so Playwright's synchronous startup cannot block the UI thread.
            ScanResult? result = await Task.Run(
                () => new ScanRunner(options, CookieCatalogue.Default(), scanLog)
                    .RunAsync(cancellation.Token),
                cancellation.Token);

            if (result is null)
            {
                scanLog.Warning("The scan found no pages, so there is nothing to report.");

                return;
            }

            ScanReportWriter.WriteFiles(options, result);
            ScanHistory.Save(result);
            ShowResult(result);
            settings.Save();
        }
        catch (OperationCanceledException)
        {
            // A cancelled scan writes no report and produces no result: a partial scan presented
            // as a complete one would be worse than no scan at all.
            scanLog.Warning("Cancelled. No report was written.");
        }
        catch (Exception error)
        {
            scanLog.Warning($"The scan failed: {error.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }
```

`BuildOptions` constructs a `ScanOptions` from the fields, reading the secret from
`Environment.GetEnvironmentVariable(ScanOptions.SecretVariable)` and throwing
`ArgumentException` with a clear message when the URL is not absolute — reusing the CLI's
own rule rather than inventing a second one.

`SetRunning(bool)` disables the inputs and Run, enables Cancel, and swaps the cursor.
`ShowResult` fills the grid with one row per candidate — Name, Storage, Category, First
seen in, Duration — giving any row whose candidate `Flag` is `Violation` a
`Firebrick` back colour and `NeedsReview` a `DarkOrange` one, then appends the
`ScanReportWriter.SummaryLines(result)` to the log so the counts are visible without
opening the report.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner.Gui/NDSTK.CookieScanner.Gui.csproj` — succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 216 passing, unaffected.

**Then run it**, with the site up:

```bash
dotnet run --project NDSTK.CookieScanner.Gui
```

Check by hand and report each: the window opens; the secret-status label is correct; Run
with the default URL produces live progress in the log; warnings appear in red; the grid
fills on completion; Cancel mid-scan returns the window to idle and logs that nothing was
written; the settings persist across a restart of the window but the member password field
comes back empty.

Run: `git status --short` — the eight files of this task.

---

## Task 8: The History tab

**Files:**
- Create: `NDSTK.CookieScanner.Gui/MainForm.History.cs`
- Modify: `NDSTK.CookieScanner.Gui/MainForm.cs` — populate the History tab

**Interfaces:**
- Consumes: `ScanHistory`, `ScanHistoryEntry`, `ScanResult` (Task 5); `ScanDiff`, `CategoryChange` (Task 6).
- Produces: nothing further.

- [ ] **Step 1: Build the tab**

`NDSTK.CookieScanner.Gui/MainForm.History.cs`, as `partial class MainForm`. A
`SplitContainer`: on the left a `ListView` in details mode with columns Completed, Site,
Entries and Exit code, filled from `ScanHistory.Default().List()` and refreshed both on tab
activation and after a scan completes — so a run just finished appears without restarting
the window. On the right, a `DataGridView` with the same columns and colouring as the Scan
tab's findings grid.

Selecting one entry loads it with `ScanHistory.Load` and fills the grid. Selecting exactly
two enables a Compare button; anything else disables it, with a label saying `Select two
scans to compare`.

Compare replaces the right pane with the diff:

```csharp
    private void OnCompareClicked(object? sender, EventArgs e)
    {
        if (SelectedEntries() is not [ScanHistoryEntry first, ScanHistoryEntry second])
        {
            return;
        }

        // Ordered by time, not by click order, so "appeared" always means "appeared in the newer
        // one" no matter which row was selected first.
        (ScanHistoryEntry older, ScanHistoryEntry newer) =
            first.CompletedAt <= second.CompletedAt ? (first, second) : (second, first);

        ScanResult? before = history.Load(older);
        ScanResult? after = history.Load(newer);

        if (before is null || after is null)
        {
            MessageBox.Show(this, "One of those scans could not be read.", "Compare",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        ShowDiff(ScanDiff.Between(before.Candidates, after.Candidates), older, newer);
    }
```

`ShowDiff` fills the right pane with three labelled groups — Appeared, Disappeared,
Recategorised — each a small grid, with a header line naming the two scans by their
completion times. When all three are empty it shows `Nothing changed between these two
scans` rather than three empty grids.

- [ ] **Step 2: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner.Gui/NDSTK.CookieScanner.Gui.csproj` — succeeded.

**Then, with the site up**, run the window and check each by hand:

1. The History tab lists the scans run in Tasks 5 and 7, newest first.
2. Selecting one fills the grid, and its contents match that scan's report file.
3. Selecting two enables Compare; one or three disables it.
4. Comparing two identical scans says nothing changed.
5. **Comparing two genuinely different scans shows the difference.** Produce the difference
   honestly rather than by editing a history file: run one scan with `--member-email`
   supplied and one without, from the console. The member run finds
   `.AspNetCore.Identity.Application` and the public one does not, so the diff must show
   exactly that one cookie as appeared or disappeared depending on the order.
6. Selecting the two in the other order gives the same answer — appeared and disappeared do
   not swap based on click order.

Run: `git status --short` — the two files of this task.

---

## Task 9: Publish, and the comparison gate

**Files:**
- Modify: `docs/cookie-scanner.md`
- No source changes expected. Any needed here is a fix to an earlier task, applied in place.

- [ ] **Step 1: The full comparison against the baseline**

This is the gate the whole refactor rests on. The baseline was captured from the
pre-refactor build and lives in `.superpowers/sdd/ui-baseline/`. **With the site up**, and
leaving a minute between runs for the consent endpoint's throttle:

```bash
# public
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 \
  --client-id cookie-scanner --dry-run --report-dir ./scan-out > /tmp/f-pub-out.txt 2> /tmp/f-pub-err.txt
# member
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 \
  --client-id cookie-scanner --dry-run --member-email 'cookie-scan-test@ndstk.local' \
  --member-password 'amRTkMr4GULF0h9Aa!' --report-dir ./scan-out > /tmp/f-mem-out.txt 2> /tmp/f-mem-err.txt
# unreachable
dotnet run --project NDSTK.CookieScanner -- --url http://localhost:59999 --max-pages 3 \
  > /tmp/f-ref-out.txt 2> /tmp/f-ref-err.txt
# bad credentials
NDSTK_COOKIESCAN_CLIENT_SECRET=deliberately-wrong-secret dotnet run --project NDSTK.CookieScanner -- \
  --url https://localhost:44351 --max-pages 3 --client-id cookie-scanner --dry-run \
  --report-dir ./scan-out > /tmp/f-bad-out.txt 2> /tmp/f-bad-err.txt

B=.superpowers/sdd/ui-baseline
diff /tmp/f-pub-out.txt $B/public-stdout.txt; diff /tmp/f-pub-err.txt $B/public-stderr.txt
diff /tmp/f-mem-out.txt $B/member-stdout.txt; diff /tmp/f-mem-err.txt $B/member-stderr.txt
diff /tmp/f-ref-out.txt $B/refused-stdout.txt; diff /tmp/f-ref-err.txt $B/refused-stderr.txt
diff /tmp/f-bad-err.txt $B/badcreds-stderr.txt
```

Every diff must be empty apart from the trailing `exit=N` line the baselines carry.
**Report each diff's output verbatim.** A difference is a regression until shown otherwise —
do not update the baseline to match. Note that the bad-credentials exit code legitimately
changed from 0 to 2 under Task 2's rule, which is an intended behaviour change and must be
called out rather than hidden.

- [ ] **Step 2: Publish both exes**

```bash
dotnet publish NDSTK.CookieScanner -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true -o dist
dotnet publish NDSTK.CookieScanner.Gui -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true -o dist
```

Report both file names and sizes. Then **verify the window runs standalone** — copy
`dist/ndstk-cookiescan-ui.exe` to a directory outside the repository, run it from there,
and complete one scan. This is the check the console tool already needed once; the GUI hits
the same Playwright asset problem and the failure only appears when the exe is moved.

- [ ] **Step 3: Update the documentation**

In `docs/cookie-scanner.md`, add a section covering: that there are now two executables and
what each is for; that the window is the one to double-click and the console tool is what CI
runs; where scan history lives (`%LOCALAPPDATA%\NDSTK.CookieScanner\scans`, capped at 50)
and that both front ends write there; that the settings file holds no credentials by design;
and the publish command for the GUI. Update the "What has been verified" section to cover
the window.

Also state plainly that `cookie-scan-report.json` changed shape — it is now a serialized
`ScanResult` so history can read it back — and that anything parsing the old format needs
updating.

- [ ] **Step 4: Final verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — 216 passing.
Run: with the site **stopped**, `dotnet build NDSTK.slnx` — succeeded, 0 warnings, 0 errors,
all five projects. Hand the site back rather than restarting it.
Run: `git status --short` — expect only `docs/cookie-scanner.md`. `dist/` must not appear.

---

## Plan Self-Review

**1. Spec coverage.** Every section maps to a task:

| Spec section | Task |
| --- | --- |
| `IScanLog`, two levels, message text unchanged | 1 |
| `ConsoleScanLog` routing Info/Warning to stdout/stderr | 1 |
| Engine files take a log | 1 |
| `ScanResult` and the exit-code rule, now testable | 2 |
| `ScanRunner` extraction, thin CLI, summary moves out | 3 |
| Cancellation | 3 (token), 7 (button) |
| Report JSON round-trips; enum names not integers | 4 |
| Scan history, location, pruning, skip-unparseable | 5 |
| `ScanDiff`, candidate lists not `ScanResult`, no globs | 6 |
| The window, Scan tab, log marshalling, settings | 7 |
| Credentials never persisted | 7 (`GuiSettings`) |
| History tab, view one, diff two | 8 |
| Publish properties for the GUI | 7 (csproj), 9 (verified) |
| The baseline comparison gate | 1, 3 (partial), 9 (full) |
| Testing list | 1, 2, 4, 5, 6 |
| Risks 1–4 | 9 Step 1, 9 Step 2, 7 Step 2, 4 |

No gaps.

**2. Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Two
places defer deliberately and say so: `ScanHistory.Save` is commented out in Task 3 with a
`TASK 5 RESTORES THIS` marker, and the History tab is empty until Task 8. Both name the
task that resolves them.

**3. Type consistency.** Cross-checked producer against consumer: `IScanLog` (1 → 3, 7);
`ScanResult` (2 → 3, 4, 5, 7, 8); `ScanRunner.RunAsync` returning `ScanResult?` with null
meaning no pages, handled in both front ends (3 → 7); `ScanReportWriter.WriteFiles` and
`SummaryLines` (3 → 7); `ScanJson` (4 → 5); `ScanHistory` and `ScanHistoryEntry` (5 → 8);
`ScanDiff.Between` taking two candidate lists (6 → 8).

One inconsistency found and fixed while reviewing: `ScanHistory` exposes both a static
`Save(ScanResult)` and an instance `SaveResult(ScanResult)`. The static one is what the CLI
and the window call; the instance one is what the tests call against a temporary folder. An
earlier draft named both `Save`, which would not compile as an overload pair distinguished
only by static-ness.
