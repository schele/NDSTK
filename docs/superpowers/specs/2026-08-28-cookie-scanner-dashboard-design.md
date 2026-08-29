# Cookie scanner dashboard — design

Date: 2026-08-28
Branch: `feature/cookie-scanner-dashboard`, from `feature/cookie-scanner-ui`
Target: NDSTK, .NET 10, WebView2 in a WinForms shell on `net10.0-windows`, over the finished scan runner

## Purpose

Replace the cookie scanner's WinForms window with a desktop dashboard that looks like a modern web
application: cards on a cool canvas, stat tiles, a findings table, a trend across past scans, a
streaming log. The WinForms window works and is verified, but it looks like 2005, and the person who
opens it occasionally — when the site changes — reads it faster when the numbers are the first thing
on the page.

Nothing about the scan changes. The engine, the report files, the history folder and the console
tool's exit codes stay exactly as they are; this is a new front end over `ScanRunner`, which exists
so that adding one is cheap.

## Scope

In scope:

- A single exe that opens its own window and renders an HTML/CSS/JS dashboard inside it. No browser
  chrome, no address bar, no tab.
- The capabilities the WinForms window already has: run a scan with the CLI's options, live progress,
  findings with violations called out, scan history, view one past scan, compare two.
- One capability the format makes natural: a **trend across history** — entries and violations per
  scan — because "did something change after that deploy" is the question the history exists for.
- **Retiring `NDSTK.CookieScanner.Gui`** in the same branch: project, solution entry, and its line in
  `NDSTK.csproj`'s `DefaultItemExcludes`.

Out of scope, and why:

- **Retiring the CLI.** Its exit code gates CI, and a GUI-subsystem process has no console to write
  one to. Unchanged, and still the only thing CI runs.
- **A browser tab.** Rejected in favour of an app window: the tool is framed as an application, and
  the CLI already covers every headless case.
- **Search, filters, notifications, date pickers.** A scan produces two to forty rows. There is
  nothing to filter and nothing to be notified about. The sidebar and the message protocol are
  shaped so a new page can be added later without disturbing the others; no page is built for a
  need nobody has yet.
- **Cross-platform.** WebView2 is Windows-only, as WinForms was. The CLI stays `net10.0` and portable.
- **Editing the cookie catalogue in the UI.** A different screen with a different shape. Later, if ever.

## Approach chosen, and the ones rejected

Four credible ways exist to put HTML in a .NET window. The choice was made from a research sweep
whose load-bearing claims were then checked against primary sources; the dated findings are in
`.superpowers/sdd/dashboard-research/`.

**Chosen: `Microsoft.Web.WebView2` hosted in a plain WinForms `Form`.** One `Form`, one docked
`WebView2`, standard title bar. Microsoft ships it monthly; its single-file publish story is
verified end to end; the loader question is settled (below); and it adds no second UI framework to
the repo. The package lists only `net462` in `lib/`, which looks alarming and is not: `build/Common.targets`
adds references from `lib_manual/netcoreapp3.0` for any TFM compatible with `net5.0`, so a
`net10.0-windows` project gets real .NET assemblies with no `NU1701`.

**Rejected: Photino.NET.** It would have been the idiomatic choice — a tiny native shell built for
exactly this. It is effectively unmaintained: last release 2025-01-23, no `net10.0` target, an
unanswered "is this project dead?" from July 2026, v1 DPI awareness with an open multi-monitor bug.

**Rejected: Avalonia + `Avalonia.Controls.WebView` 12.1.0.** Genuinely attractive — MIT, `net10.0`-native,
and its *managed* WebView2 loader removes `WebView2Loader.dll` from the publish question entirely.
It loses because it drags a whole cross-platform UI stack (Skia, HarfBuzz, ANGLE, AXAML) into a
single-window Windows-only app. It is the fallback if WinForms hosting ever hits a wall.

**Rejected: an embedded Kestrel serving `http://localhost:port`.** A second server, a port to pick,
a firewall prompt to explain, and CORS/CSP to reason about — all to replace two method calls.

## Architecture

| Project | Kind | Responsibility |
| --- | --- | --- |
| `NDSTK.CookieScan.Core` | classlib, `net10.0` | unchanged: rules, `ScanDiff`, zero dependencies |
| `NDSTK.CookieScanner` | exe, `net10.0` | unchanged engine + CLI, plus two additive record fields (below) |
| `NDSTK.CookieScanner.Desktop` | exe, `net10.0-windows` | **new**: the window and the dashboard |
| `NDSTK.CookieScanner.Gui` | — | **deleted** |
| `NDSTK.Tests` | xunit | plus tests for the two record additions |

`NDSTK.CookieScanner.Desktop` takes the retired project's assembly name, **`ndstk-cookiescan-ui`**,
so the published exe, the publish command and every line of `docs/cookie-scanner.md` stay as they
are. It references `NDSTK.CookieScan.Core` and `NDSTK.CookieScanner` directly — the Core reference
is added explicitly rather than inherited transitively, because the front end names `Locale`,
`CookieDeclarationCandidate` and `CandidateFlag` in its own signatures.

The dependency runs one way, as before: the window references the scanner, never the reverse, and
`NDSTK.CookieScanner` must not reference WinForms or WebView2.

### The shell

```csharp
// Program.cs
ApplicationConfiguration.Initialize();
Application.Run(new DashboardForm());
```

`DashboardForm` is a `Form` whose whole client area is one `WebView2`, sized 1280×860 logical through
`LogicalToDeviceUnits` (the WinForms window taught us that raw pixels render at two-thirds size on a
150% display), `MinimumSize` likewise, `PerMonitorV2`.

Startup order matters and is not negotiable:

1. `CoreWebView2Environment.GetAvailableBrowserVersionString()` inside `try`/`catch (WebView2RuntimeNotFoundException)`.
   On failure: a plain `MessageBox` naming the Evergreen runtime and its download link
   (`https://go.microsoft.com/fwlink/p/?LinkId=2124703`), then exit. The runtime ships with Windows 11
   and is usually present on 10, but "usually" is not "always" and a silent crash is the worst outcome.
2. `CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions())`
   where `userDataFolder` is `%LOCALAPPDATA%\NDSTK.CookieScanner\webview2`, then
   `EnsureCoreWebView2Async(environment)`. **The default user-data folder is created next to the exe**
   and fails outright when the exe sits in `Program Files` or on a read-only share — which is exactly
   where a portable exe ends up.
3. Harden the control: `AreDefaultContextMenusEnabled`, `IsZoomControlEnabled`, `IsPinchZoomEnabled`,
   `IsSwipeNavigationEnabled`, `IsStatusBarEnabled`, `AreBrowserAcceleratorKeysEnabled`,
   `IsPasswordAutosaveEnabled`, `IsGeneralAutofillEnabled` all `false`; `AreDevToolsEnabled` only
   under `#if DEBUG`. This is an application, not a browser.
4. Register the asset handler, then navigate.

### Serving the dashboard

The dashboard's files are `EmbeddedResource`s with `LogicalName` preserving their relative path.
One filter, using the three-argument overload (the two-argument one is deprecated and misbehaves for
iframes):

```csharp
core.AddWebResourceRequestedFilter(
    "https://app.localhost/*",
    CoreWebView2WebResourceContext.All,
    CoreWebView2WebResourceRequestSourceKinds.All);
```

`WebResourceRequested` maps the path to a resource stream and answers with
`CreateWebResourceResponse(stream, 200, "OK", "Content-Type: …\r\nCache-Control: no-store")`, or 404
for a miss. The window navigates to `https://app.localhost/index.html`.

**The host name is `app.localhost`, and that is a measured decision.** A made-up name under `.local`
costs a ~2 second DNS resolution timeout on every navigation — reproduced in a NetLog, confirmed by
a WebView2 maintainer; `.test` and `.example` are no better. Names under `.localhost` resolve in
11–79 ms.

Why not the alternatives:

- **`NavigateToString`** caps at 2 MB, and gives the document a `null` origin — no `localStorage`,
  not a secure context, no relative subresources. It cannot serve a font.
- **`SetVirtualHostNameToFolderMapping`** is faster (resolved inside WebView2's own processes) but
  needs real files on disk, which means relying on `IncludeAllContentForSelfExtract`'s extraction
  directory — a mode Microsoft documents as "not recommended" — and a mapped host cannot also be
  intercepted, so `/api/*` would be closed to us forever.

Serving from embedded resources costs a few dozen UI-thread requests at startup and nothing after.

### The bridge

One JSON envelope in each direction. Host to page via `CoreWebView2.PostWebMessageAsJson`; page to
host via `window.chrome.webview.postMessage` and `WebMessageReceived`. Not `AddHostObjectToScript`:
COM ceremony, slower, and Microsoft's own guidance prefers web messages.

Page → host: `ready`, `run(options)`, `cancel`, `listHistory`, `loadScan(path)`,
`compare(pathA, pathB)`, `openReportFolder`.

Host → page: `state { running, pass, totalPasses }`, `log { level, message }`,
`result { scan }`, `history { entries }`, `scan { result }`, `diff { … }`, `error { message }`.

A new page later adds a message; it does not touch the others. That is the whole extensibility story,
and it is structure rather than speculation.

**Messages posted before the page has loaded are dropped**, so the host buffers into a bounded queue
and flushes it when `ready` arrives.

### Threading

`WebViewScanLog : IScanLog` serialises the envelope on the calling thread — `IScanLog` is called
from Playwright's threads — then marshals with `BeginInvoke`, guarding `IsHandleCreated`/`IsDisposed`
and swallowing `ObjectDisposedException` from the race between the guard and the post. The WinForms
window had to learn this the hard way; the reasoning transfers verbatim.

Cancellation is unchanged in shape: a `CancellationTokenSource` per run, `Task.Run` around
`RunAsync`, `cancel` calls `Cancel()` on the UI thread, `catch (OperationCanceledException)` **before**
`catch (Exception)`, disposal in `finally` after `SetRunning(false)`. The UI says plainly that a
cancel takes effect at the end of the current pass, because that is what the engine honours.

## The window

A 196 px sidebar — brand, **Scan**, **History**, and a footer showing the last scan and how many are
kept — beside the page.

### Scan

Top row, side by side: a **Run a scan** card (site URL, an "Options" disclosure holding max pages,
locale, member email, member password and client id, a Dry run checkbox defaulting to *on*, and Run)
and the **trend** card (entries and violations per scan, with the current entry count pulled out
large). Under them a band of four stat tiles — entries found, violations, needs review, expected but
not observed. Below, the findings table full width: name, storage, category, first seen in, duration,
and a state pill.

Pressing Run turns the run card into the live log **in place** — nothing navigates. A progress line
names the pass, Cancel replaces Run, and the previous scan's tiles, chart and table stay on screen
dimmed rather than blanking for the fifty seconds a scan takes. On completion the log keeps its
scrollback, the summary lines are appended, and the tiles, chart and table update.

Beside the client-id field, the same notice the WinForms window carried: whether
`NDSTK_COOKIESCAN_CLIENT_SECRET` is set, read once at startup.

### History

Kept scans newest first: completed, site, entries, and the result as a **word** — "clean",
"1 violation", "write-back failed" — rather than the exit code's number, which stays in the report
and the row's tooltip. Tick one to show it in the same findings table the Scan page uses. Tick two to
enable Compare, which names the pair it is about to compare.

The comparison shows appeared, disappeared and recategorised, ordered by completion time so
"appeared" always means "in the newer one" regardless of click order. Empty groups collapse to one
sentence instead of an empty grid; all three empty says so plainly.

## Two additive fields on the record

The final review of the WinForms work found that two scans run with *different options* diff as
though the *site* had changed — the showcase comparison, where a member scan and a public scan differ
by `.AspNetCore.Identity.Application`, is exactly that artefact presented as the feature working.
The information needed to warn about it is not in the file.

So `ScanResult` gains one nullable nested record:

```csharp
public sealed record ScanOptionsSummary(
    int MaxPages, Locale Locale, bool MemberScanEnabled, bool DryRun);

// on ScanResult, last positional member:
ScanOptionsSummary? Options
```

Nullable, like `Outcome`, so a history file written before this change loads with `Options: null` and
the comparison says "options were not recorded for the older scan" instead of a false all-clear. The
shape check in `ScanJson.Deserialize` is unaffected — it validates the collections, and this is a
nullable reference.

And `ScanHistoryEntry` gains `ViolationCount`, which costs nothing: `ScanHistory.List()` already
deserialises every file and currently throws that number away. The trend needs it for its second
series, and without it the chart would have to load all fifty files again.

Both are additive, both are round-trippable, and both get tests beside the existing ones.

## The look

Light, airy, cool. Cards on an off-white blue-grey canvas, soft blue-tinted shadows, 14–16 px radii.
The accent family is **navy and teal**; red is reserved for violations and amber for needs-review and
log warnings, because a compliance failure has to be the loudest thing on the page and cannot be if
it shares a family with the furniture.

```
--canvas:#F4F7FC   --surface:#FFFFFF   --surface-2:#F9FBFE
--border:#E2E9F4   --border-strong:#CFDBEC
--ink-900:#16202E  --ink-600:#516079   --ink-500:#6F7E96  --ink-400:#95A2B8
--blue-600:#1D4ED8 --blue-800:#16389E  --blue-100:#E8EDFD --blue-50:#F2F5FE
--teal-600:#0F8B7A --teal-800:#0A6357  --teal-50:#E3F5F1
--amber-700:#9A6300 --amber-50:#FFF6E3
--red-600:#C2334B  --red-50:#FDECEF
--log-bg:#0F1826   --log-ink:#C6D2E4   --log-warn:#FFC978
radii 6 / 10 / 12 / 14 / 999   spacing 4·8·12·16·20·24·32·40
shadow-sm 0 1px 2px rgba(22,32,46,.04), 0 2px 6px rgba(29,78,216,.06)
shadow-md 0 4px 12px rgba(22,32,46,.06), 0 1px 3px rgba(22,32,46,.04)
```

Type: **Inter Variable**, self-hosted as one `woff2` (~48 KB, OFL 1.1, licence file beside it),
falling back to Segoe UI Variable Text. Google Fonts is not an option — the window must work with no
internet. Numerals use `tabular-nums`. The log panel is dark and monospaced (Cascadia Mono,
Consolas): a terminal reads better dark, and it separates machine output from the rest of the page.

**No build step.** Plain HTML, CSS and ES modules, with Lit 3.3.x vendored as a single
`vendor/lit.js` — the same library and the same no-decorator style as this repo's existing Umbraco
backoffice extension, so nothing new is introduced. `dotnet publish` stays the only build; the CI
agent needs no npm. Components: `<cs-stat-tile>`, `<cs-findings-table>` (a real `<table>`),
`<cs-trend-chart>` (inline SVG, `vector-effect: non-scaling-stroke`, two `<path>` series over a
gradient fill), `<cs-log-panel>`, `<cs-history-list>`, `<cs-diff-view>`. A hand-rolled SVG chart
rather than Chart.js or uPlot: at most fifty points, no interaction beyond a tooltip.

Accessibility is native-elements-first: real `<button>`, `<table>` with `<th scope>`, `<label for>`;
the sidebar is a `<nav>` with `aria-current`; the log is `role="log" aria-live="polite"` with
warnings prefixed by a word, not only a colour; `:focus-visible` rings everywhere; Ctrl+Enter runs,
Escape cancels; `prefers-reduced-motion` disables transitions. Log auto-scroll only when already at
the bottom, so reading scrollback mid-scan is not fought.

## Settings, and what is never persisted

`%LOCALAPPDATA%\NDSTK.CookieScanner\settings.json` keeps its existing path and its six fields
(`Url`, `MaxPages`, `Locale`, `MemberEmail`, `ClientId`, `DryRun`) so a user's remembered settings
survive the swap, with `JsonStringEnumConverter` as the WinForms window ended up doing. Saved when
the options are built — the run against a mistyped URL is exactly when the typed value is worth keeping.

**The client secret and the member password are never persisted**, and no field exists that could
hold either. The secret comes only from `NDSTK_COOKIESCAN_CLIENT_SECRET`; the password is typed per
run into an `<input type="password" autocomplete="off">`. The CLI refuses a `--client-secret` flag so
a secret cannot reach shell history, and a settings file storing one would undo that.

Reports go to `%LOCALAPPDATA%\NDSTK.CookieScanner\reports`, history to `…\scans`, both as now, with
the two writes **independently guarded** so a locked report file cannot cost the history entry or the
exit code.

## Publishing

```
dotnet publish NDSTK.CookieScanner.Desktop -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

`WebView2Loader.dll` is bundled by `IncludeNativeLibrariesForSelfExtract` alone — the bundler
classifies by content, not MSBuild item type. `IncludeAllContentForSelfExtract` is needed only
because Playwright's driver ships non-PE content, and it is what makes `AppContext.BaseDirectory`
point at `%TEMP%\.net\<app>\<hash>` rather than beside the exe. `EnableCompressionInSingleFile`
measured −58% for +1.3 s on first launch only: a 213 MB exe becomes roughly 90 MB, which matters when
copying it to another machine. No trimming, no AOT — Playwright and WebView2 both use reflection.

`Microsoft.Web.WebView2` is pinned to **1.0.4129.50** (2026-08-03). 1.0.4191.47 released the day this
was written; it can be taken once it has had a week.

**Anything resolved "beside the exe" must use `Path.GetDirectoryName(Environment.ProcessPath)`, never
`AppContext.BaseDirectory`.** `CatalogueSource.Load` currently gets this wrong, which means the
documented catalogue-override file cannot work in either published exe; it is fixed on the branch
this one starts from.

## Testing

Unit tested in `NDSTK.Tests`:

- `ScanOptionsSummary` round-trips through `ScanJson`, and a file written without it loads with
  `Options: null`.
- `ScanHistoryEntry.ViolationCount` reflects the loaded result.
- The asset resolver: a request path maps to the expected embedded resource, an unknown path is a
  404, and every file the dashboard references resolves — a test that would have caught a font
  renamed but not re-embedded.
- The message protocol: each page→host envelope deserialises into its command, and an unknown `type`
  is ignored rather than throwing.

Not unit tested, consistent with the existing exemption: the form, Playwright, HTTP, and the
dashboard's own JavaScript.

## Verification, which is the real gate

1. The full suite green.
2. **The CLI's output unchanged**, by the harness this repo already uses: the pre-change CLI built
   from a worktree and this branch's CLI, same site, minutes apart, four scenarios (public, member,
   refused, bad credentials), byte-identical stdout, stderr and markdown report, matching exit codes.
   The engine gains two record fields; the console tool must not notice.
3. **The dashboard driven end to end through UI Automation**, as the WinForms window was: the window
   opens, a scan streams progress, the grid matches the report JSON, warnings render as warnings,
   Cancel returns to idle leaving *no* new file in `reports` or `scans`, settings persist with no
   credential in the file, History lists and compares, and the comparison's group assignment does not
   depend on click order. WebView2 exposes the page's DOM to UIA, so the same technique applies.
4. **A screenshot of each state, read and judged** — idle, mid-scan, result, history, comparison.
5. **Published single-file, copied to a directory outside the repository, and run from there** —
   the check that once caught the CLI's missing Playwright assets, and which now also proves the
   embedded dashboard and the redirected user-data folder.
6. **Run once from a read-only location** (a copy under `C:\Program Files`), which is the case the
   default user-data folder fails.

## Risks

1. **The WebView2 runtime is absent on some machine.** Handled by the startup check and a message
   naming the download, rather than a crash. It cannot be bundled without shipping a fixed-version
   runtime, which would add ~150 MB.
2. **First launch extracts ~200 MB to `%TEMP%\.net`** and old extractions are never cleaned up.
   Documented, with `DOTNET_BUNDLE_EXTRACT_BASE_DIR` named as the escape hatch for AppLocker- or
   antivirus-hostile machines.
3. **Log lines from Playwright's threads.** Every write marshals; the guard is not atomic with the
   post, so `ObjectDisposedException` is caught explicitly. The failure only appears when the window
   closes mid-scan, which is the worst place to discover it.
4. **`prefers-color-scheme`.** The dashboard commits to light. The WebView will report the system
   scheme; the CSS ignores it deliberately rather than shipping a half-tested dark theme.
5. **Retiring a verified window.** The WinForms front end was proven end to end. Everything it does
   is re-proven by step 3 before it is deleted, and its deletion is the last commit on the branch,
   not the first.
