# Cookie scanner desktop UI — design

Date: 2026-08-28
Branch: to be created from `master`
Target: NDSTK, .NET 10, WinForms on `net10.0-windows`, over the finished cookie scanner

## Purpose

Give the cookie scanner a window. Today it is a console tool: `--url` is required, so
double-clicking the exe prints an error into a console that closes before it can be read,
and running it means remembering flags. The person running this is a developer doing it
occasionally, when the site changes — exactly the case where remembered flags are the
friction.

The window runs a scan and shows its findings, and it keeps past scans so two runs can be
compared. "A cookie appeared after that deploy" is the question the history exists to
answer; a list of reports nobody diffs would not be worth building.

## Scope

In scope:

- A desktop window that runs a scan with the same options the CLI takes, shows live
  progress, and presents the findings with violations called out.
- Scan history: every run recorded, any past run viewable, any two comparable.
- A shared scan runner both front ends drive, so the window and CI cannot diverge.
- Replacing the scanner's console writes with an injected log abstraction.

Out of scope, and why:

- **Retiring the CLI.** Its exit code is what lets CI gate a build, and a GUI-subsystem
  process has no console to write to. The console tool stays exactly as capable as it is.
- **One exe that is both.** Making a single binary behave as a window when double-clicked
  and as a console tool when piped needs `AttachConsole` handling that behaves differently
  in a real pipeline than in testing. That trades a verified CI path for tidiness.
- **Editing the cookie catalogue in the UI.** A separate screen with a different shape.
  Worth doing later; not part of this.
- **Cross-platform UI.** WinForms is Windows-only. The CLI stays `net10.0` and portable;
  only the new project is Windows-bound.
- **Scheduling or unattended runs.** That is what the CLI and CI are for.

## Approach chosen, and the one rejected

Two ways existed to get the scanner's 31 console messages into a window.

**Rejected: redirect the console.** The window sets `Console.SetOut`/`SetError` to a writer
that marshals to the UI thread, and all 31 call sites stay untouched. Lowest churn through
code that was reviewed line by line, and the window would receive every diagnostic without
anything being routed by hand. Rejected on the grounds that it relies on process-global
state, cannot express severity beyond stdout-versus-stderr, and leaves the scanner's
diagnostics permanently un-testable.

**Chosen: an injected log abstraction.** Every console write becomes a call on an
`IScanLog` passed in. The CLI implements it against `Console`; the window implements it
against its log pane. The cost is real and is accepted deliberately: six files that were
each verified end to end get edited, and every touched line is an opportunity to regress a
message somebody checked. That cost is controlled by the verification requirement below,
not waved away.

**Because this refactor touches verified code, it is not complete until a live scan has
been re-run against a real site and its output compared against the recorded output from
the current implementation.** A green test suite is not sufficient evidence here: the test
suite never covered these messages, which is part of why the abstraction is wanted.

## Architecture

| Project | Kind | Responsibility |
| --- | --- | --- |
| `NDSTK.CookieScan.Core` | classlib, `net10.0` | unchanged, plus `ScanDiff` |
| `NDSTK.CookieScanner` | exe, `net10.0` | scan engine, `IScanLog`, `ScanRunner`, CLI front end |
| `NDSTK.CookieScanner.Gui` | exe, `net10.0-windows` | the window |
| `NDSTK.Tests` | xunit | tests for `ScanDiff`, `ScanResult.ExitCode`, the history store |

`NDSTK.CookieScanner` must not reference WinForms. It stays `net10.0` so the CLI can still
be published for a non-Windows build agent. The dependency runs one way: the window
references the scanner, never the reverse.

### `IScanLog`

```csharp
namespace NDSTK.CookieScanner;

/// <summary>Where the scan's running commentary goes.</summary>
/// <remarks>
/// Injected rather than written straight to the console, because the same scan drives a console
/// tool and a window. Two levels only: <see cref="Info"/> is progress a reader expects, and
/// <see cref="Warning"/> is something that went wrong but did not stop the scan - a page that
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

Two implementations: `ConsoleScanLog` (writes `Info` to stdout, `Warning` to stderr,
preserving today's behaviour exactly) and, in the GUI project, one that appends to the log
pane.

Every existing `Console.WriteLine` becomes `log.Info(...)`; every `Console.Error.WriteLine`
becomes `log.Warning(...)`. **The message text does not change.** The refactor moves where a
string goes, never what it says — that is what makes the before-and-after comparison in the
verification step meaningful.

Classes taking a log: `SiteCrawler`, `PageCapture`, `ConsentPassRunner`, `MemberDimension`,
`ManagementApiClient`, `BrowserBootstrap`. `PageCapture`'s methods are static and take it as
a parameter; the rest take it as a constructor dependency, matching how they already take
`ScanOptions`.

### `ScanRunner` and `ScanResult`

`Program.cs`'s try-block becomes a reusable runner:

```csharp
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
    public int ExitCode { get; }
}

public sealed class ScanRunner(ScanOptions options, CookieCatalogue catalogue, IScanLog log)
{
    public Task<ScanResult> RunAsync(CancellationToken cancellationToken);
}
```

`Program.cs` shrinks to: parse arguments, build a `ConsoleScanLog`, run, write the report
files, print the summary, return `result.ExitCode`.

**The exit-code rule moves onto `ScanResult` and gains a test.** It currently lives inside
`ScanReportWriter.Write`, a method that also writes two files and prints a summary — which
is exactly why the whole-branch review recorded it as untestable. The rule is unchanged:
violations outrank everything and return 1; a write-back that was configured, attempted and
failed returns 2; anything else returns 0. A missing credential is not an error, because
report-only is a supported mode.

`ScanReportWriter` keeps writing the two report files. Printing the summary to the console
moves to the CLI, because the window shows its results in a grid and would otherwise
receive a console-formatted duplicate of what it already displays.

### Cancellation

`RunAsync` takes a `CancellationToken` and honours it between passes and between page
visits. A cancelled scan writes no report and produces no result — a partial scan reported
as though complete would be worse than no scan, which is the same reasoning that already
makes a failed navigation contribute nothing.

## Scan history

Every completed run writes its result to
`%LOCALAPPDATA%\NDSTK.CookieScanner\scans\<utc-timestamp>.json`, in addition to whatever
`--report-dir` receives. Both front ends write it, so a CLI run shows up in the window's
history. No database and no second format: a scan's findings are the record.

**`cookie-scan-report.json` becomes a serialized `ScanResult`.** Today it is an anonymous
object assembled inline by `ScanReportWriter`, which is fine to read and cannot be
deserialized back into anything. Since history must load a past scan into the same grid a
live one uses, the file has to round-trip. So both the report file and the history file
become the same serialized `ScanResult`, written by the same code.

This changes the report JSON's shape — a breaking change for anything already parsing it.
Nothing does today, and `cookie-scan-report.md` is unaffected. The one field that
disappears as a top-level key is `needsReview`, which is derivable from `candidates` by
flag; the markdown report keeps rendering its own section from exactly that derivation.

`ScanHistory` handles the folder: list entries newest first, load one, and prune to the
most recent 50. Pruning happens on write, after the new entry is saved, so a failed prune
can never cost the scan that just ran. By count rather than age, because "the last fifty
scans" is comprehensible in a way "ninety days" is not when you scan irregularly.

The JSON currently encodes `ConsentPass` two ways — enum names for the hosts dictionary,
integers for `FirstSeenPass`. The whole-branch review recorded this as a deferred minor;
history reads these files back, so it is fixed here with `JsonStringEnumConverter`. **Files
written before that change will not load**, so `ScanHistory` skips an entry it cannot parse
and logs a warning rather than failing the list.

## `ScanDiff`

Pure, in Core, no dependencies, unit-tested like every other rule:

```csharp
public sealed record ScanDiff(
    IReadOnlyList<CookieDeclarationCandidate> Appeared,
    IReadOnlyList<CookieDeclarationCandidate> Disappeared,
    IReadOnlyList<CategoryChange> Recategorised)
{
    public static ScanDiff Between(
        IReadOnlyList<CookieDeclarationCandidate> older,
        IReadOnlyList<CookieDeclarationCandidate> newer);
}

public sealed record CategoryChange(string Name, string From, string To);
```

It takes the two candidate lists rather than two `ScanResult`s, deliberately. `ScanResult`
lives in `NDSTK.CookieScanner`, and Core must not reference the scanner — the dependency
runs one way, which is what keeps Core free of Playwright, HTTP and Umbraco. The caller
passes `older.Candidates` and `newer.Candidates`, which is all a diff needs anyway.

Matching is by name, case-insensitively, consistent with the rest of the codebase.
**Deliberately not matched as globs**: two scans of the same site produce names from the
same catalogue, so a pattern in one is a pattern in the other, and glob matching here would
report a pattern and a literal that happen to overlap as unchanged when one genuinely
replaced the other.

A recategorisation is the interesting finding — a cookie moving from `necessary` to
`marketing` between runs means the site changed what it does with it.

## The window

One form, two tabs.

**Scan.** Fields for site URL, max pages, locale, member email and password, and API client
id; a dry-run checkbox; Run and Cancel. Below, a log pane fed by the GUI's `IScanLog`, with
warnings visually distinct. On completion, a grid of findings — name, storage, category,
first-seen pass, duration — with violations highlighted, plus the added / already-declared
/ declared-but-not-found counts.

**History.** Past scans listed newest first with date, site, entry count and exit code.
Selecting one shows it in the same grid. Selecting two enables Compare, which shows the
three diff lists.

The scan runs on a background task so the window stays responsive. The GUI's `IScanLog`
marshals to the UI thread — `IScanLog` is called from Playwright's threads, and appending
to a control from one directly would throw.

### Settings, and what is never persisted

Last-used URL, page cap, locale and client id are saved to
`%LOCALAPPDATA%\NDSTK.CookieScanner\settings.json`.

**The client secret and the member password are never persisted.** The secret continues to
come only from `NDSTK_COOKIESCAN_CLIENT_SECRET`, and the member password is typed per run
into a masked field. This is not a detail: the CLI deliberately refuses a `--client-secret`
flag so a secret cannot reach shell history, and a settings file that stored one would undo
that for the sake of saving a paste.

If the environment variable is absent, the Scan tab says so plainly next to the client id
field, rather than letting a run fail at the token request.

## Testing

Unit tested in `NDSTK.Tests`:

- `ScanDiff.Between` — appeared, disappeared, recategorised; case-insensitive matching;
  an empty older scan meaning everything appeared; identical scans producing three empty
  lists.
- `ScanResult.ExitCode` — the four cases: no credentials and no violations → 0; no
  credentials with violations → 1; credentials with a successful write-back → 0;
  credentials with a failed write-back → 2. This closes a gap the whole-branch review
  recorded.
- `ScanHistory` — writes and lists newest-first; prunes past 50; skips an unparseable file
  with a warning instead of throwing.

Not unit tested, consistent with the existing exemption: the form, Playwright, and HTTP.

## Verification, which is the real gate

1. The full suite green.
2. **A live scan against the local site, its console output compared line by line against
   the output recorded before the refactor.** Any difference in wording, ordering or
   stream is a regression until proven otherwise.
3. The same scan from the window: same findings, progress visible, violations highlighted.
4. A second run, then a diff of the two in the History tab.
5. Cancel mid-scan: the window returns to idle, no report is written, nothing is left
   running.
6. The GUI published as a single file and launched from a directory outside the repository
   — the same check the CLI needed, since it hits the same Playwright asset problem.

## Risks

1. **The refactor regresses a diagnostic.** The mitigation is step 2 above, not the test
   suite, which never covered these strings.
2. **The GUI exe misses the publish properties.** `PublishSingleFile` and
   `IncludeAllContentForSelfExtract` are both required; without the second, Playwright's
   `node.exe` is left behind and the exe fails the moment it is copied anywhere. This
   already caught the CLI once.
3. **Log calls from Playwright's threads.** Every GUI log write must marshal; one that does
   not will throw an invalid-cross-thread exception, and it may only show up under a
   failure path that is rarely exercised.
4. **History files from before the enum-encoding fix will not parse.** Handled by skipping
   and warning, but it means the first runs after this ships start the history fresh.
