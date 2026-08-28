# Cookie scanner

An audit tool for the cookie consent banner. It drives a real, headless browser through the site
six times, once per consent decision a visitor can make, and compares what actually gets set
against what the policy page already declares. Anything set outside the consent it needed is a
**violation**; anything set but undeclared becomes a **draft** addition to the policy page for an
editor to review and publish.

Four projects carry this: `NDSTK.CookieScan.Core` (pure rules — catalogue matching, category
inference, the violation rule, duration formatting; no Umbraco, no Playwright, no HTTP, so it is
unit tested without either), `NDSTK.CookieScanner` (the engine and its console front end,
`ndstk-cookiescan`: crawling, the six passes, the report, the write-back client, and scan history),
`NDSTK.CookieScanner.Gui` (`ndstk-cookiescan-ui`, a WinForms window built on that same engine — a
Scan tab and a History tab in place of stdout, nothing about the scan itself different), and
`CookieScan/` inside the site itself (the merge endpoint the tool posts its findings to, and the
API user that authenticates the post).

## What it does, and what it deliberately does not do

- It runs Chromium **on the machine that runs the tool**, never on the production server. The
  published exe is meant to be copied to a laptop and pointed at a URL; nothing about it runs
  in-process with the site.
- It never deletes or rewrites an existing declaration. The merge is append-only by construction —
  see `MergePlanner.Plan` and `CookieScanWriter.Append` — because a declaration's purpose text is
  legal wording an editor may have hand-written, and a tool that silently rewrote it would be worse
  than no tool.
- It never publishes. A successful write-back calls `IContentService.Save`, never `Publish`. A
  placeholder purpose on an unrecognised cookie must not become public legal text without a human
  reading it first.
- It never declares a `Pixel`. The storage-type dropdown the CookieBanner package offers has a
  `Pixel` option, but the scanner has no way to detect a tracking pixel from what a browser exposes
  and does not attempt to guess — see Known limitations, below.
- The only two forms it ever submits are the member login form and the cookie-consent decision
  itself (via the site's own `/api/cookie-consent` endpoint). Nothing here books a class, cancels a
  booking, registers a child, or completes a payment — see the TempData limitation below for why
  that matters.
- `/umbraco` is excluded from the crawl outright (`SiteCrawler.Exclusions.IsExcluded`). Backoffice
  cookies are not a visitor's cookies and have no business on a public policy page.

## The window

`NDSTK.CookieScanner` publishes as `ndstk-cookiescan.exe`, the console tool — unchanged in
behaviour, and what CI runs. `NDSTK.CookieScanner.Gui` publishes as `ndstk-cookiescan-ui.exe`, the
window — and the one to double-click. Both run the same `ScanRunner`, the same six passes, the
same violation rule and the same catalogue; the console tool prints what `ScanRunner` and
`ScanReportWriter` produce, and the window puts the same `ScanResult` into a grid instead.
`ScanRunner`'s own remarks say why this is one class rather than two similar ones: a window that
found something different from what CI gates on would be worse than no window.

The window has two tabs. **Scan** takes the same options as the CLI's flags — site URL, max
pages, locale, member email and password, client id — runs the scan with live progress in a log
pane (warnings in red), and fills a findings grid where violations are firebrick and
`NeedsReview` candidates are dark orange (`MainForm.Colour`). **History** lists every past scan
newest-first (see Scan history, below), shows any one of them in the same grid, and compares any
two — appeared, disappeared and recategorised cookies, as three groups from `ScanDiff.Between`
(in `NDSTK.CookieScan.Core`) — with the pair ordered by completion time rather than by which row
was clicked first.

Two differences from the CLI are deliberate. **Dry run defaults to on** in the window
(`GuiSettings.DryRun` defaults `true`), so the obvious button to press cannot write to a live
policy page; the console tool still defaults it off, because a CI invocation names every flag
explicitly. And the window remembers its options between runs — URL, max pages, locale, member
email, client id, the dry-run flag — in `%LOCALAPPDATA%\NDSTK.CookieScanner\settings.json`, while
the console tool takes flags fresh every time. **The client secret and the member password are
never written to that file** — see The client secret environment variable, below, for why.

A `cookie-catalogue.json` beside *either* exe replaces the embedded catalogue exactly as before —
see Overriding the catalogue, below.

## Scan history

`cookie-scan-report.json` changed shape in this work: it is now `ScanResult` serialized directly
(`ScanJson.Serialize`), camelCase, with enums written as names rather than integers —
`ScanJson`'s own reasoning is that the file is meant to be readable, and an integer would silently
change meaning if a `ConsentPass` or `CandidateFlag` member were ever reordered. The write-back
section is now keyed `outcome` (was `merge`) and the third-party-hosts section is
`hostsByPass` (was `hosts`); the top-level `needsReview` array is gone — it is
`ScanResult.NeedsReview`, a `[JsonIgnore]`d property derived from `candidates` by
`flag == "NeedsReview"`, not a second copy of the same information to keep in step — and
`canReachApi`, `dryRun` and `completedAt` are new. The same change reaches inside every
`CookieDeclarationCandidate` nested in `candidates` and `violations`, too: those objects went
PascalCase to camelCase along with everything else (`"Name"` → `"name"`), and their `flag` is now
a name rather than a number (`"Flag": 0` → `"flag": "None"`) — the change most likely to break a
script that parsed the old report, since it is buried inside every entry rather than sitting at
the top level. **Anything that parsed the old shape needs updating.** `cookie-scan-report.md` is
unchanged.

The reason for the change is `ScanHistory`: every completed run, from either front end, writes
this same JSON to `%LOCALAPPDATA%\NDSTK.CookieScanner\scans\<utc-timestamp>-<suffix>.json`, capped
at the most recent 50 (`ScanHistory.Keep`) and pruned *after* the write completes, specifically so
a prune that fails cannot cost the scan that just finished. A file that will not parse is skipped
when history is listed, not fatal to the rest of the list — the folder holds files this code did
not necessarily write itself, so one bad one must not cost the whole list. Both front ends write
to the same folder, so a scan run from the command line shows up in the window's History tab.

Scan history is not the same thing as the report files. The console tool's report still goes to
`--report-dir` as before (`cookie-scan-report.md` and `.json`, current directory by default); the
window's own copy of those same two files goes to `%LOCALAPPDATA%\NDSTK.CookieScanner\reports`
instead, because a window's current directory is wherever it happened to be launched from — a
desktop shortcut leaves it at the system directory — and reports written there would scatter or
fail to write.

## The six passes

Each pass opens a fresh browser context (empty cookie jar), posts one real consent decision to the
site's own endpoint, then replays the same fixed URL list — discovered once, before any pass runs,
so "first seen in pass N" always means the browser genuinely had not visited that page under any
other consent state first.

| Pass | Consent granted | An entry first appearing here proves |
| --- | --- | --- |
| `Undecided` | Nothing (no decision posted at all) | The site sets it before a visitor has chosen anything — must be `necessary` |
| `RejectAll` | Nothing (explicit refusal) | The site sets it even after an explicit refusal — must be `necessary` |
| `Preferences` | `preferences` | The site sets it once preferences are granted |
| `Statistics` | `statistics` | The site sets it once statistics are granted |
| `Marketing` | `marketing` | The site sets it once marketing is granted |
| `AcceptAll` | `preferences`, `statistics`, `marketing` | Everything was granted, so this alone proves nothing about *which* category it belongs to — these entries are flagged `NeedsReview` rather than confidently categorised |

A seventh dimension, `MemberArea`, runs outside this list: it signs in and crawls the member portal,
a different URL set entirely, so its findings cannot be compared by pass order against the six. An
entry found only there is implicitly `necessary` — a cookie that only exists once you are signed in
is a session artefact by construction.

### The violation rule

`CategoryInference.Classify` decides a cookie's category from the pass it first appeared in, then
`ViolationScan.Find` separately checks **every** sighting in the raw observation list — not just
the earliest one per name — against what that sighting's pass had granted. A catalogued category
appearing in a pass that did not grant it is a consent violation, full stop, whether the visitor
refused outright or granted something unrelated (`necessary` is exempt: it is implied rather than
granted, so it never needs to appear in a granted set).

This is deliberately generalised across passes 1–5: a marketing cookie set once during `RejectAll`
and then set *again* during `Statistics` is flagged both times, because a tag that respects consent
selectively — obeying it in one pass and ignoring it in another — is exactly the failure mode the
six passes exist to catch. Reducing to "the earliest sighting per name" before checking would lose
the second violation entirely; that reduction is used for the *declarations* list, never for
violations.

## Flags

All flags are `--name value`, except `--dry-run` and `--headed`, which take no value. Verified
against `NDSTK.CookieScanner/ScanOptions.cs`.

| Flag | Required | Default | Meaning |
| --- | --- | --- | --- |
| `--url` | Yes | — | The site to scan. Must be an absolute URL with a scheme. Also the base the crawl's own-host check and the `/umbraco` exclusion are relative to. |
| `--target` | No | same as `--url` | The address the token request and merge POST are sent to, when it differs from `--url` — e.g. crawling the public hostname while the management API is reached over a different address. Also what the loopback check for the self-signed-certificate bypass looks at. |
| `--max-pages` | No | `25` | The page cap per pass (and for the member-area crawl). Any non-positive or unparsable value is silently replaced by the default rather than rejected. |
| `--locale` | No | `sv` | `sv` or `en`. Anything other than exactly `en` (case-insensitive) is treated as Swedish. Governs the wording written into new declarations and the duration text's language. |
| `--member-email` | No | — | Together with `--member-password`, enables the member-area dimension. Both must be set or neither is used. |
| `--member-password` | No | — | See above. |
| `--client-id` | No | — | The API user's client id. Together with the secret in the environment, enables the write-back comparison against the live policy page. |
| `--dry-run` | No | off | Runs the full comparison against the site but writes nothing. Requires `--client-id` and the secret to have any effect — without those the scan is already report-only. |
| `--report-dir` | No | current directory | Where `cookie-scan-report.md` and `cookie-scan-report.json` are written. |
| `--headed` | No | off | Runs Chromium with a visible window instead of headless. For watching a scan run, not for CI. |

The client secret is **not** a flag — see below.

The window's Scan tab takes the same options through fields rather than flags —
`MainForm.BuildOptions` builds exactly the `ScanOptions` a CLI invocation with the same values
would have — with two deliberate omissions: no field for `--target` (the window always compares
against the host it scanned) and none for `--headed` (the window always runs headless; `--headed`
exists to watch the engine while debugging it, which is not what a window is for).

## The API user

The write-back endpoint (`CookieScanController.Merge`, at
`/umbraco/management/api/v1/cookie-scan/merge`) is authorised with
`[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`, unchanged from how Task 13 left it.
**Whether an API-user token actually satisfies that policy has not been exercised end to end** —
see Not yet verified, below.

`CookieScanApiUserSeeder` creates the user in code, on every boot, and is entirely opt-in:

```json
// appsettings.Development.json
{
  "NDSTK": {
    "CookieScanApiUser": {
      "Enabled": true,
      "ClientId": "cookie-scanner"
    }
  }
}
```

```json
// appsettings.Secrets.json (gitignored)
{
  "NDSTK": {
    "CookieScanApiUser": {
      "ClientSecret": "…"
    }
  }
}
```

**This creates a real credential with content access.** `NDSTK:CookieScanApiUser:Enabled` belongs
in development configuration only — `appsettings.Development.json` and
`appsettings.Secrets.json` — and must never be set in `appsettings.json`, which ships to every
environment. With `Enabled` false, or with `Enabled` true but no secret configured, the seeder does
nothing at all: it logs a warning and returns, and boot is never blocked by it (every failure path
in `CookieScanApiUserSeeder.SeedAsync` is caught and logged, not thrown).

The user is created with `UserKind.Api`, joined to the `editor` group by default
(`UserGroupAliases`), and approved immediately. Content access is what the merge endpoint's
authorisation needs; nothing about the scanner needs Settings or Users access.

**Rotating the secret**: change the value in `appsettings.Secrets.json` and restart the site. The
seeder's `applicationManager.EnsureBackOfficeClientCredentialsApplicationAsync` call is safe to
repeat — on the next boot it re-registers the client id with the new secret against the OpenIddict
application store, with no manual step in the backoffice. Update the copy the scanner uses
(`NDSTK_COOKIESCAN_CLIENT_SECRET`, below) to match, or the next scan's token request fails with 401.

## The client secret environment variable

```
NDSTK_COOKIESCAN_CLIENT_SECRET=<secret>
```

Read once, in `ScanOptions.Parse`, straight from `Environment.GetEnvironmentVariable`. This is
deliberately not a `--client-secret` flag: an argument passed on the command line ends up in shell
history and in any process listing (`ps`, Task Manager, a CI log that echoes its invocation) for as
long as either persists. An environment variable set for one shell session, or injected by a CI
secret store directly into the process environment, does not have either problem.

The window applies the same rule to itself. `GuiSettings` persists everything else the Scan tab
holds, but has no member for the client secret or the member password — the secret still only ever
comes from `NDSTK_COOKIESCAN_CLIENT_SECRET`, read straight into `MainForm.BuildOptions` and shown
nowhere, and the label beside the client-id field says plainly whether that variable is currently
set rather than offering a field that would let a secret reach `settings.json`. The member password
is typed fresh for every run for the same reason. A settings file that persisted either would have
undone, to save a paste, exactly what refusing `--client-secret` was for.

## Publishing the portable exes

### The console tool

```
dotnet publish NDSTK.CookieScanner -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o dist
```

Produces `dist/ndstk-cookiescan.exe`, roughly 180 MB (172 MiB) as a single self-contained file —
verified against Windows x64, `net10.0`.

**Chromium itself is not inside the exe.** `BrowserBootstrap.EnsureChromium` shells out to
Playwright's own installer on every run, which is a no-op once a build is already cached but
downloads it the first time — roughly 150–300MB, into `%LOCALAPPDATA%\ms-playwright`, and needs
internet access for that first run only. A first run on a new machine appearing to hang for a
minute at "Checking for a Chromium build..." is this download, not a fault.

**Both single-file properties baked into the csproj are required, and one of them is not obvious.**
`IncludeNativeLibrariesForSelfExtract` is the documented requirement — without it, Playwright's
native libraries cannot be loaded from inside a single-file bundle and the exe fails at the first
browser launch. That alone was not sufficient here: Playwright's browser *driver* — a bundled
Node.js runtime plus its own JavaScript files, normally deployed as a `.playwright\` folder of loose
content beside the build output — is content, not a native library, so `dotnet publish` still left
it behind as loose files next to the exe. Copied anywhere without that folder, the exe built and ran
but failed immediately with:

```
Microsoft.Playwright assembly was found, but is missing required assets. Please ensure to build
your project before running Playwright tool.
```

`NDSTK.CookieScanner.csproj` also sets `IncludeAllContentForSelfExtract`, which bundles that content
into the single file too and extracts it next to the exe on first run. Confirmed by publishing and
then running the exe from a directory containing nothing else, per the Verified section below.

### The window

```
dotnet publish NDSTK.CookieScanner.Gui -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o dist
```

Produces `dist/ndstk-cookiescan-ui.exe`. Same three properties, baked into
`NDSTK.CookieScanner.Gui.csproj` for the same reason as above: the window references
`NDSTK.CookieScanner` and therefore Playwright, so it hits exactly the same driver-is-content
problem the console tool does, and the failure only shows up once the exe is copied somewhere
without the loose `.playwright\` folder beside it.

## Overriding the catalogue

A `cookie-catalogue.json` placed beside either exe replaces the embedded catalogue **wholesale** —
it is not merged with the built-in one. `CookieCatalogue.Parse` reads it with
`PropertyNameCaseInsensitive`, comment and trailing-comma tolerant, from
`AppContext.BaseDirectory` — `CatalogueSource.Load` is the one place both front ends call this
from, so an override means the same thing regardless of which exe is running. Verified against
`NDSTK.CookieScan.Core/CatalogueEntry.cs` and the shipped
`NDSTK.CookieScan.Core/Resources/cookie-catalogue.json`:

```json
{
  "unknownCategory": "marketing",
  "entries": [
    {
      "pattern": "_ga_*",
      "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
      "category": "statistics",
      "purpose": {
        "sv": "Håller reda på sessionen för en enskild Google Analytics-egenskap.",
        "en": "Keeps session state for one Google Analytics property."
      },
      "durationDays": 730,
      "tracker": true,
      "expected": false
    }
  ]
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `pattern` | string, required | The cookie/storage-key name to match. May use `*` as a wildcard; `CookieCatalogue.Match` picks the most specific match — fewest wildcard characters absorbed, then the longest literal prefix — when more than one pattern matches. |
| `provider` | `{ "sv": …, "en": … }`, required | Who sets it, in both shipped languages. Never generated at runtime — this becomes public legal wording. |
| `category` | string, required | One of `necessary`, `preferences`, `statistics`, `marketing`. |
| `purpose` | `{ "sv": …, "en": … }`, required | Why it's set, in both languages, written straight onto the policy page. |
| `durationDays` | number, optional | The documented lifetime, in days. **`0` means a session cookie. Absent (not present in the JSON) means "use whatever expiry the browser actually reported"** — the catalogue's own number wins when present, because a browser may cap or truncate what it reports. |
| `tracker` | bool, default `false` | Informational metadata only; not currently read by any scan or report logic. |
| `expected` | bool, default `false` | Marks an entry as belonging to *this site's own stack*, so its absence from a scan is itself worth reporting under "Expected but not observed". Leave `false` for third-party entries — an absent Google cookie is normal and not worth flagging. |
| `unknownCategory` (top level, not per-entry) | string, default `marketing` | The category assigned to a name no pattern matches, when the pass it appeared in implies nothing (`AcceptAll`) — see the `NeedsReview` row in the passes table. |

## Exit codes

| Code | Constant | Meaning |
| --- | --- | --- |
| `0` | `ScanResult.ExitCode` | No consent violations found, and no configured write-back failed. |
| `1` | `ScanResult.ExitCode` | One or more consent violations found. |
| `2` | `ScanResult.ExitCode`, or `ScanReportWriter.ExitError` | Either a write-back was configured and attempted but never produced an outcome, or the scan itself could not complete at all — a bad URL, no reachable pages, a Chromium launch failure, and so on. |

Both front ends return the same number for the same reason: `ScanResult.ExitCode` is a property on
the shared result, not something either one recomputes for itself. The window has no process to
exit, but the same rule is what colours its findings grid and drives the History tab's exit-code
column.

**The exit code reflects findings, never configuration.** A report-only run — no `--client-id`, no
secret — that finds a violation still exits `1`; a missing credential can never mask a violation.
`Violations.Count > 0` wins outright before anything else is even considered, so a violation always
exits `1` regardless of what the write-back did. Below that: a write-back that was configured
(`CanReachApi`) and attempted (there were candidates to send) but left `Outcome` null exits `2`.
A merge-endpoint failure (a 401, a validation rejection) is logged and swallowed inside
`ManagementApiClient` rather than thrown — the scan still completes and its report is still
written — but it is not silent: the null `Outcome` it leaves behind is exactly what turns the run's
exit code into `2`, which is what lets a CI job gate on the exit code alone rather than parsing
output for the word "failed". `ScanReportWriter.ExitError` is the same `2` for the separate case
where the scan never got that far — bad arguments, no pages discovered, or an unhandled exception —
returned directly by `Program.cs` before there is a `ScanResult` to ask.

## Known limitations

- **`.AspNetCore.Mvc.CookieTempDataProvider` is declared as `expected` but the crawl will not find
  it.** ASP.NET Core only sets that cookie on a request that actually writes TempData — a booking,
  a cancellation, a child-management action, or a completed registration — and every one of those
  is a POST. The scanner's six passes and the member dimension only ever issue GETs, deliberately:
  `MemberDimension`'s own remarks are explicit that login is the only form it submits, because the
  scanner must not be able to create real bookings, cancellations or payments on a live site. The
  practical effect is that this cookie will always show up under "Expected but not observed" rather
  than being found — declared deliberately in the catalogue rather than something to chase by
  teaching the scanner to submit more forms.
- **No pixel detection.** Third-party hosts contacted during each pass are captured and reported
  (the report's "Third-party hosts contacted" section), so a tracking pixel's *host* is visible, but
  nothing infers a `Pixel` storage-type declaration from that. `StorageKinds` documents that the
  scanner never emits `Pixel` even though the CookieBanner package's dropdown offers it — that
  inference is out of scope by design, not an oversight.

## What has been verified

Everything below was exercised against a real Umbraco 18.1.1 site (`https://localhost:44351`),
not reasoned about. Where a claim rests on reading rather than running, it says so.

**The refactor**

The engine was pulled out from under the console tool and shared with the window across several
tasks; the claim that matters most is that neither front end's behaviour drifted while that
happened. Proven by running the pre-refactor and post-refactor console tool against the same live
site, 75 seconds apart, across four scenarios — public, member, a refused connection, and bad
credentials — and diffing stdout, stderr and the markdown report for each: byte-identical in all
four, with matching exit codes, bad credentials included (`2` on both sides). Bad credentials
exiting `2` rather than `0` — described under Exit codes, above — was decided during the scanner's
original development, before this refactor; this comparison is what confirms the refactor itself
changed nothing further.

**The scan itself**

- All six consent passes run, each in a fresh browser context, each posting its decision to the
  site's own `/api/cookie-consent` endpoint. The passes genuinely differ: the antiforgery cookie
  appears in `Undecided`, the consent cookie only from `RejectAll` onwards. That difference is what
  proves the decisions are being recorded rather than the same state being measured six times.
- Discovery, the page cap, and the `/umbraco` and sign-out exclusions.
- Both report files, in Swedish and under `--locale en`.
- The exit codes, including that a configured write-back which fails exits `2` rather than `0`.
- A failed write-back still writes the report. Proven by pointing the tool at a site whose API user
  did not yet exist: the token 401'd, the failure was reported with an actionable message, and the
  report was written anyway.

**The member dimension**

- Login against a real member account, the member-area crawl, and the attribution of what it finds
  to the `MemberArea` pass. This found `.AspNetCore.Identity.Application` — a cookie the site sets
  and the policy page did not declare.

**The write-back**

- An API-user client-credentials token against
  `/umbraco/management/api/v1/security/back-office/token` — form-encoded, HTTP 200.
- The merge endpoint. `POST .../cookie-scan/merge` returns 401 rather than 404 for an
  unauthenticated caller, so the route is mapped and the authorisation policy resolves; an
  authenticated call returns 200 and writes.
- **The block actually lands correctly.** Read from the database rather than inferred: `layout`,
  `contentData` and `expose` all grew from 3 entries to 4, the new block's key is present in all
  three, and `category` and `storageType` are stored as `["necessary"]` and `["Cookie"]` — the
  serialized array form the flexible dropdown requires. A block missing from `expose` would save
  and then never render, with no error anywhere, so this was the single most important thing to
  confirm.
- **Append-only.** The three pre-existing blocks were byte-identical and unmoved afterwards, and
  the page's `heading`, `introduction` and `outro` were untouched — the whole-document-replace trap
  the narrow endpoint exists to avoid.
- **Save, never publish.** The page came out in state `PublishedPendingChanges`, i.e. a draft.
- **Idempotence**, twice over: an identical second call added nothing and reported `saved: false`;
  and a real suffixed antiforgery cookie matched the declared `.AspNetCore.Antiforgery.*` pattern
  rather than duplicating it. Without that, every run would re-add the same cookie forever.

**The violation rule**

Exercised through `ViolationScan.Find` directly, with a `_fbp` cookie observed during the
`Undecided` pass: one violation, category `marketing`. Not exercised through a real browser,
because that would mean adding a tracker to a live site; the browser layer above it is covered by
the six-pass runs.

**The console tool's portable exe**

Published, and run from a directory outside the repository with nothing else in it — Chromium
bootstrap, crawl, report. The `IncludeAllContentForSelfExtract` property is what makes that work;
without it the exe runs from its own build folder and fails with a missing-assets error the moment
it is copied anywhere, which is the only situation a portable exe is for.

**The window's portable exe**

Published the same way — `dist/ndstk-cookiescan-ui.exe`, roughly 213 MB single-file — then copied
to a directory outside the repository and run from there: all six passes appeared in the log, the
run finished with `2 entr(ies) found.`, both report paths were written, and the findings grid
filled. No Playwright assets error and no orphan process left behind, which is the same failure
this check exists to catch — see Publishing the portable exes, above.

**The window**

Driven end to end through UI Automation, not just opened and looked at: live progress appearing in
the log pane as a scan runs, with warnings rendered in red; the findings grid filling to match the
report JSON; a cancelled run leaving neither a report file nor a history entry behind; settings
persisting across a restart with no credential ever written to `settings.json`; and the History
tab's compare showing exactly the one cookie that differs between a member scan and a public scan,
placed in the same group (Appeared, Disappeared or Recategorised) regardless of which of the two
rows was selected first.

## What has not been verified

- **A full scan against production.** `ndstk.se` is currently running a build without the cookie
  banner: no policy page, no `/api/cookie-consent`, and the package's own `consent.js` returns 404.
  A scan there reaches pass 2 and stops. That is a fact about what is deployed, not a defect — the
  same command against a site that has the banner completes. Re-run it once production is updated.
- **A site with real third-party tags.** This site loads none, so the categorisation of a genuine
  statistics or marketing cookie, and the violation rule firing on a real tracker in a browser,
  have not been seen end to end. The logic is unit-tested and the mechanism is proven; the input
  has never existed here.
- **`.AspNetCore.Mvc.CookieTempDataProvider` being observed.** It is only set by a request that
  writes `TempData` — a booking, cancellation, child-management or registration POST — and the
  crawl issues only GETs. It appears under "expected but not observed" by design; declare it
  deliberately rather than waiting for a scan to find it.

## Troubleshooting

**`invalid_client` / "The specified 'client_id' is invalid" from the token endpoint.**
The OpenIddict application was never registered. The seeder did not run, or it failed —
check the boot log for `CookieScanApiUserSeeder`. Remember it only runs when both
`NDSTK:CookieScanApiUser:Enabled` is true and a `ClientSecret` is configured.

**`Authorization failed` / "The user associated with the supplied 'client_id' could not be
found", with the seeder reporting success.**
This one is worth knowing about, because nothing in the log points at it. Umbraco's
back-office token handler prepends `umbraco-back-office-` to the incoming `client_id`
before resolving it to a user. So the user↔client-id association must be stored with the
prefix, while the OpenIddict application and the value you pass to `--client-id` stay
unprefixed. The seeder handles this; if you ever write your own, the raw id will register
an application fine and then fail at user resolution with exactly this message. Verified
against Umbraco 18.1.1 by decompiling `BackOfficeUserClientCredentialsManager.FindUserAsync`
and its `SafeClientId` helper, and against the `umbracoUser2ClientId` table.

**`UserNameIsNotEmail` when the API user is created.**
Umbraco validates a user's username as an email address, so an API user's username cannot
be its client id. The seeder uses the configured email; the client id is attached
separately.

**The endpoint works but does not appear in `/umbraco/swagger`.**
Known and cosmetic. `POST .../cookie-scan/merge` returns 401 for an unauthenticated caller
rather than 404, so the route is mapped. Why it is absent from the swagger document is
unresolved; it serves a CLI, not people browsing swagger.
