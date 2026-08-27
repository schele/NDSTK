# Cookie scanner — design

Date: 2026-08-27
Branch: `feature/cookie-scanner`
Target: NDSTK, Umbraco 18.1.1 on .NET 10, `Esatto.Umbraco.Backoffice.CookieBanner` 1.1.1

## Purpose

Discover, with a real browser, every cookie the site actually sets; work out each one's
consent category from evidence rather than from a guess at its name; and add the ones that
are missing to the cookie policy page as a draft an editor reviews and publishes.

The policy page's declarations are maintained by hand and nobody knows whether they are
true. Today the page holds only the three the CookieBanner package seeds — the consent
cookie, `.AspNetCore.Antiforgery.*` and `UMB_MEMBER` — because the seeder deliberately stops
there and leaves the rest, in its own words, "to an editor and to the scanner package".

The list is already known to be incomplete. Every booking controller writes `TempData`,
which sets `.AspNetCore.Mvc.CookieTempDataProvider` on the front end, and that cookie is not
declared. A declaration list that is quietly wrong is a compliance problem, and hand
maintenance guarantees it drifts the moment anyone adds an analytics tag or an embed.

## Scope

In scope:

- A crawl of the site with a real browser, capturing cookies, `localStorage` and
  `sessionStorage`.
- Six consent passes, so the pass an entry first appears in establishes its category.
- Detection of the case that matters most: something non-necessary set before, or in spite
  of, a refusal.
- An optional member-login dimension, since two of the site's cookies exist only behind a
  login.
- Append-only write-back into the policy page's Block List, saved as a draft.
- A report, and an exit code CI can gate on.
- A portable single-file `.exe` runnable on any Windows 11 machine.

Out of scope, and why:

- **Chromium on the production web server.** The scan never runs in the site process. A
  browser in the app's container is 300MB of image and a genuine OOM risk during a scan no
  visitor asked for.
- **Modifying or deleting existing declarations.** Append-only. Editor-written purpose text
  is legal wording and must not be clobbered, and a declaration can be correct while simply
  not having been triggered by one crawl.
- **Publishing.** The scan leaves a draft. A placeholder purpose must not become public
  legal text without a human reading it.
- **Pixel and tag declarations.** Third-party hosts contacted are reported; nothing is
  inferred from them into a declaration.
- **Form submissions beyond login and the consent decision.** The scanner must not be able
  to create a real booking or a Swish payment.
- **`/umbraco`.** Backoffice cookies do not belong in a visitor-facing policy.
- **A backoffice dashboard.** A read-only drift panel is a cheap later addition; it needs no
  browser and nothing here forecloses it.

## Verified platform facts

Checked against the real assemblies in the local NuGet cache, not read from documentation —
the same caution the CookieBanner package's own compatibility notes record, since every
mismatch here throws at runtime rather than failing to compile.

- `ManagementApiControllerBase`, `VersionedApiBackOfficeRoute` and `BackOfficeAccess` all
  exist in `Umbraco.Cms.Api.Management` 18.1.1, so a site-owned Management API controller
  with API-user token auth is available.
- The token endpoint is `/umbraco/management/api/v1/security/back-office/token`, and
  `UserClientCredentials` exists — confirming the client-credentials grant for API users.
- `UpdateDocumentRequestModel` carries `Cultures` and `Template` alongside a
  `DocumentValueModel` collection. A generic document `PUT` is therefore a **whole-document
  replace**: omit a property and it is erased. This is why the write path is a narrow
  site-owned endpoint rather than the generic document endpoint.
- The CookieBanner seeder builds its Block List value from Umbraco's own `BlockListValue`,
  `BlockItemData`, `BlockListLayoutItem`, `BlockPropertyValue` and `BlockItemVariation`, and
  populates an `Expose` list its comment describes as what "marks the blocks as visible".
- That seeder writes dropdown values through a helper whose comment states the flexible
  dropdown "always stores an array, even in single-value mode".
- `LoginSurfaceController` reports login failures through `ModelState`, not `TempData`. There
  is therefore no safe read-only action that triggers the TempData cookie. See Limitations.
- The site currently loads **no third-party scripts or embeds at all**: no
  `GoogleMeasurementId` configured, no `<consent-script>` or `<consent-embed>` in any view,
  no external URL in the views or static assets.

## Architecture

Four pieces — three new projects plus an addition to the web project.

| Project | Kind | Responsibility | Depends on |
|---|---|---|---|
| `NDSTK.CookieScan.Core` | classlib, `net10.0` | catalogue, category inference, glob matching, duration formatting, merge planning | nothing |
| `NDSTK.CookieScanner` | exe, `net10.0` | Playwright crawl, consent passes, token and HTTP write-back, report | Core, `Microsoft.Playwright` |
| `CookieScan/` inside `NDSTK.csproj` | site code | Management API controller, Umbraco-side merge writer | Core, Umbraco |
| `NDSTK.Tests` | xunit | unit tests for all of Core | Core |

Core exists so that no interesting rule needs a browser or a published content graph to be
tested. This follows the reasoning already recorded in `NDSTK.Domain.csproj`: keeping the
rules out of the web project makes the absence of an Umbraco dependency a compiler guarantee
rather than a matter of discipline, and keeps the test suite independent of the web assembly,
which a running site holds a file lock on.

### Registration chores that fail silently when missed

- Add `NDSTK.CookieScan.Core\**;NDSTK.CookieScanner\**` to `DefaultItemExcludes` in
  `NDSTK.csproj`. The comment there states the rule already: the web project sits at the
  repository root, so the SDK's default globs would otherwise compile the sibling projects'
  sources, and their `obj/` output, into the web assembly. "Any further project added beside
  this one has to be listed here too."
- Add both projects to `NDSTK.slnx`.
- Reference `NDSTK.CookieScan.Core` from `NDSTK.csproj`, `NDSTK.CookieScanner` and
  `NDSTK.Tests`.

## The scan

### URL discovery happens once

Breadth-first from `--url`, following same-host links only. Excluded: anything under
`/umbraco`, `mailto:`, `tel:`, `javascript:`, fragment-only links, non-HTML responses, and
any URL whose path contains a segment in the sign-out list — `logout`, `logga-ut`,
`signout` — since following one mid-crawl would silently end the member session and make
every later page in that pass anonymous. Capped by `--max-pages`, default 25.

That URL list is then **replayed identically in every pass**. This is a correctness
requirement, not an optimisation: if each pass discovered its own URLs, an entry appearing
"first in pass 4" might only mean pass 4 was the first to visit the page that sets it, which
would corrupt every category inference the design rests on.

### Six passes, each in a clean browser context

Each pass gets a fresh Playwright browser context, so the cookie jar starts empty. The
decision for a pass is **posted to the site's real `/api/cookie-consent` endpoint from inside
that context**, not forged with `context.AddCookiesAsync`. The package writes that cookie
server-side precisely so its attributes are right; a hand-made cookie risks a shape the site
rejects, and the scan would then silently measure the undecided state six times over.

| # | Pass | Decision posted | Meaning of an entry first seen here |
|---|---|---|---|
| 1 | `undecided` | none | Set before any choice exists. Must be strictly necessary. |
| 2 | `reject-all` | `{"action":"reject-all"}` | Necessary. |
| 3 | `preferences` | `{"action":"custom","categories":["preferences"]}` | Preferences. |
| 4 | `statistics` | `{"action":"custom","categories":["statistics"]}` | Statistics. |
| 5 | `marketing` | `{"action":"custom","categories":["marketing"]}` | Marketing. |
| 6 | `accept-all` | `{"action":"accept-all","categories":["preferences","statistics","marketing"]}` | Only reachable through a combination. Flagged for review. |

`accept-all` sends the full category list explicitly, because the package's endpoint grants
exactly the set it is given and does not read "all" from an omission.

The endpoint throttles at `ThrottleRequestsPerMinute`, default 10, per IP. Six passes make
six posts, inside that budget — so the passes must stay sequential rather than being
parallelised into more posts within one minute.

### Capture

After each page load, collect `context.CookiesAsync()` (name, domain, path, expires,
httpOnly, secure, sameSite), then the `localStorage` and `sessionStorage` keys via
`page.EvaluateAsync`. Hosts contacted are recorded from request events, for the report only.
Results are unioned per pass, keyed by name and storage type, retaining the URL where each
was first seen.

### Member area, optional

`--member-email` and `--member-password` add a seventh dimension after pass 6: a fresh
context, accept-all posted, log in through the login form, then a **second bounded
breadth-first discovery starting from the member portal**, under the same `--max-pages` cap
and the same exclusion list. It is a separate discovery rather than a replay of the public
list, because the pages of interest — the portal, bookings, children — are only linked once
signed in. That is also why this dimension sits outside the six comparable passes: its URL
set differs, so its findings are attributed on their own terms rather than by pass order.

Login is the only form the scanner submits besides the consent decision. Entries found only
here default to `necessary` unless the catalogue says otherwise, on the grounds that a
session cookie behind a login is necessary by construction.

## Category inference

The **earliest pass an entry appears in decides its category**, per the table above.

One override sits on top of that, and it is the whole reason for scanning rather than
guessing:

> If the catalogue gives a name a category, and the entry appears in a pass where that
> category was **not** granted, that is a **consent violation**. The declaration is written
> with the catalogue's category, the finding is reported first, and the process exits
> non-zero.

Stated as passes: a tracker in pass 1 or 2 violates a refusal outright, and a statistics
cookie appearing in pass 3 — where only `preferences` was granted — violates it just as
plainly. The rule covers passes 1 through 5 uniformly; pass 6 grants everything, so nothing
there can violate.

Entries first seen in pass 6 cannot be attributed to one category. They take the catalogue's
category when the name is known. When it is not, they take the catalogue's configured
unknown-fallback category, get a purpose line saying the category needs review, and are
listed in the report's needs-review section.

## The catalogue

`cookie-catalogue.json`, embedded in the assembly as the default and overridden by a file of
that name beside the exe when present. It is data rather than code because its `purpose` text
becomes public legal wording on the policy page, and that must be editable without a rebuild.

```json
{
  "unknownCategory": "marketing",
  "entries": [
    {
      "pattern": "_ga_*",
      "provider": "Google Analytics",
      "category": "statistics",
      "purpose": "Skiljer besökare åt för att mäta hur webbplatsen används.",
      "duration": "24 månader",
      "tracker": true,
      "expected": false
    }
  ]
}
```

- `pattern` — a name pattern. `*` is the only wildcard. Matched case-insensitively.
- `tracker` — marks the entry non-necessary, for the violation rule above.
- `duration` — optional. When present it overrides the observed expiry: a documented vendor
  lifetime is more honest than one browser's observation of it.
- `expected` — this entry is known to apply to *this* site's stack, so its absence from a
  scan is itself worth reporting. This is what populates the report's "expected but not
  observed" section; without the flag that section would have nothing to draw on. Set on the
  framework entries — antiforgery, TempData, `UMB_MEMBER`, the consent cookie — and left
  `false` on every third-party entry, whose absence is normal and not a finding.

Pattern selection is **most specific wins**. Of all matching patterns, the one matching the
fewest wildcard characters is used, ties broken by the longer literal prefix — so `_ga_*`
beats `_ga*` beats `*`.

Found names collapse onto the pattern they matched, so `_ga_ABC123` and `_ga_XYZ789` produce
one `_ga_*` declaration rather than a block per Google Analytics property.

Seed contents. First-party and framework: the configured consent cookie name,
`.AspNetCore.Antiforgery.*`, `.AspNetCore.Mvc.CookieTempDataProvider`, `.AspNetCore.Culture`,
`UMB_MEMBER`, `ASP.NET_SessionId`. Third-party, all marked `tracker`: `_ga`, `_ga_*`, `_gid`,
`_gcl_au`, `_fbp`, `_hj*`, and the YouTube and Vimeo sets.

Generated wording defaults to Swedish, since the site is Swedish. `--locale sv|en` selects
it; both ship.

## Duration formatting

Applied when the catalogue supplies no `duration`, from the observed expiry.

| Observed | Written (sv) | Written (en) |
|---|---|---|
| session, no expiry, or expiry already past | `Session` | `Session` |
| under 24 hours | `N timmar` | `N hours` |
| under 60 days | `N dagar` | `N days` |
| 60 days or more | `N månader` | `N months` |

Rounding is to the nearest whole unit, with a floor of 1 — so a 90-minute cookie reads
`2 timmar` and a 30-minute one reads `1 timme`, never `0`. Months are computed as days / 30.44
and rounded, so a 365-day cookie reads `12 månader` rather than `12,0`. Singular and plural
forms differ in both locales (`1 timme` / `2 timmar`, `1 dag` / `2 dagar`, `1 månad` /
`2 månader`), because this text is read by visitors on a public page.

`localStorage` entries have no expiry and are written `Tills den raderas` / `Until deleted`,
not `Session`. That distinction is the point of recording a storage type at all.

## Write-back

### Authentication

The tool takes a token from `POST /umbraco/management/api/v1/security/back-office/token`
using the client-credentials grant of an Umbraco API user. `--client-id` is a flag; the
secret is read from the `NDSTK_COOKIESCAN_CLIENT_SECRET` environment variable and is never
accepted as a flag, so it cannot land in shell history.

### Endpoint contract

`POST /umbraco/management/api/v1/cookie-scan/merge`, bearer-authenticated.

Request:

```json
{
  "declarations": [
    {
      "name": "_ga_*",
      "provider": "Google Analytics",
      "category": "statistics",
      "purpose": "Skiljer besökare åt för att mäta hur webbplatsen används.",
      "duration": "24 månader",
      "storageType": "Cookie"
    }
  ]
}
```

Response:

```json
{
  "added": ["_ga_*"],
  "alreadyDeclared": ["UMB_MEMBER"],
  "declaredButNotFound": ["ndstk-consent"],
  "policyPageKey": "<guid of the resolved cookiePolicy node>",
  "saved": true
}
```

### Server-side merge

`CookieScanController : ManagementApiControllerBase`, decorated
`[VersionedApiBackOfficeRoute("cookie-scan")]` and authorised with the backoffice-access
policy.

The policy page is resolved exactly as the package resolves it:
`Esatto:CookieBanner:PolicyPageKey` when set, otherwise the first published node of content
type alias `cookiePolicy`.

The merge **does not hand-build Block List JSON**. It deserializes the existing `cookies`
value into Umbraco's own `BlockListValue` and appends through `BlockItemData`,
`BlockListLayoutItem` and `BlockItemVariation`, mirroring the shape the CookieBanner seeder
already uses successfully. Three specifics are easy to get wrong and silent when wrong:

1. A new block must be added to **`Layout`, `ContentData` and `Expose`**. The seeder's
   comment describes `Expose` as what marks blocks visible; omitting it saves the block but
   does not render it.
2. `category` and `storageType` are written as **serialized single-element arrays**, because
   the flexible dropdown always stores an array even in single-value mode.
3. The write is `contentService.Save(...)`. Never `Publish`.

### Guardrails

- Append-only. Never updates, deletes or reorders an existing block.
- Touches only the `cookies` property of the resolved node.
- `400` when the resolved node's content type is not `cookiePolicy`.
- `400` when a declaration's `category` or `storageType` is outside the package's known
  values.
- At most 50 blocks added per call, so a runaway scan cannot bloat the node. Exceeding it is
  a `400` with nothing written — **not** a partial save of the first 50. A scan producing 50+
  new declarations means something is wrong with the scan or the catalogue, and half-applying
  it would leave the page in a state nobody chose and make the next run's diff meaningless.
- When nothing is new: `saved: false`, empty `added`, and no save at all.

### Idempotence

A candidate counts as already declared when its name matches an existing declaration
**treated as a glob**. That is what lets a found `.AspNetCore.Antiforgery.ABC123` recognise
the already-seeded `.AspNetCore.Antiforgery.*` rather than duplicating it. Matching is
case-insensitive and runs both directions: candidate-as-pattern against existing name, and
existing-name-as-pattern against candidate.

## Report

Written to the report directory as `cookie-scan-report.md` and `cookie-scan-report.json`, and
summarised as a console table.

1. **Violations** — non-necessary entries seen in pass 1 or 2. First, because it is the
   finding that matters.
2. **Added** — declarations written to the draft.
3. **Already declared** — found, and already on the page.
4. **Declared but not found** — reported, never deleted.
5. **Needs review** — first seen in pass 6 with an unknown name.
6. **Expected but not observed** — catalogue entries flagged `expected` that the crawl did
   not see. `.AspNetCore.Mvc.CookieTempDataProvider` is expected here; see Limitations.
7. **Third-party hosts contacted**, per pass.

Exit codes: `0` clean, `1` violations found, `2` scan or write-back error.

## CLI surface

| Flag | Default | Meaning |
|---|---|---|
| `--url` | required | Root URL to crawl |
| `--max-pages` | 25 | Page cap for discovery |
| `--locale` | `sv` | Language of generated wording |
| `--member-email`, `--member-password` | none | Enables the member-area dimension |
| `--client-id` | none | Umbraco API user client id; enables write-back |
| `--target` | value of `--url` | Umbraco base URL for write-back, when it differs from the scanned site |
| `--dry-run` | off | Scan and report, write nothing |
| `--report-dir` | working directory | Where the two report files go |
| `--headed` | off | Show the browser, for debugging a scan |

Write-back happens only when `--client-id` is given **and**
`NDSTK_COOKIESCAN_CLIENT_SECRET` is set. With either absent the tool reports and skips the
write: report-only is the safe default, not an error, so a missing credential does not by
itself change the exit code. The exit code always reflects the scan's findings — a
report-only run that found a violation still exits `1`.

## The portable exe

```
dotnet publish NDSTK.CookieScanner -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

`IncludeNativeLibrariesForSelfExtract` is required: Playwright ships native libraries a
single-file bundle otherwise cannot load at runtime.

Chromium is **not** inside the exe — it lives in `%LOCALAPPDATA%\ms-playwright`. On start the
program checks for it and, if absent, runs Playwright's own installer itself, saying what it
is doing and why. That download is roughly 150MB and is the only part of a fresh-machine run
that needs internet; every later run reuses it.

## Testing

Test-driven, all in `NDSTK.Tests` against `NDSTK.CookieScan.Core`.

- `CookieNameMatcherTests` — `*` globbing, case-insensitivity, matching in both directions,
  `.AspNetCore.Antiforgery.*` against a real generated name, and that a wildcard-free name
  never matches a different name.
- `CategoryInferenceTests` — earliest pass wins; a catalogued category appearing in a pass
  that did not grant it is a violation and keeps the catalogue's category, covering pass 1
  and 2 refusals *and* the statistics-in-preferences-pass case; nothing in pass 6 can
  violate; an unknown name in pass 6 becomes needs-review; a member-area-only entry defaults
  to necessary.
- `CookieCatalogueTests` — most-specific-pattern-wins including the `_ga_*` / `_ga*` / `*`
  ordering; unknown-name fallback; a catalogue `duration` overriding an observed expiry; the
  `expected` flag selecting exactly the entries eligible for the expected-but-not-observed
  report and excluding third-party ones.
- `DurationFormatterTests` — session, hours, days and months boundaries in both locales;
  singular against plural forms; the floor-of-1 rule for a sub-minute expiry; a 365-day
  cookie reading as 12 months; and `localStorage` getting "until deleted" rather than
  "Session".
- `MergePlannerTests` — no duplicate for an already-declared name; no duplicate for a name
  covered by an existing pattern; two GA property cookies collapsing to one pattern; the
  50-block cap rejecting outright rather than partially applying; declared-but-not-found
  reporting; an empty result when nothing is new.

The Playwright and HTTP layers are not unit tested. They are verified by a real run against
the local site, checked against the report.

## Risks to verify during implementation

Each of these fails at runtime rather than at compile time, which is why they are called out
rather than assumed.

1. The exact constant for the backoffice-access authorisation policy, and whether a
   **client-credentials API user token satisfies it** or whether it admits only an
   interactive backoffice session. If the latter, the endpoint needs a policy that accepts an
   API user, and the authentication section above changes.
2. The token endpoint's exact request encoding — form fields against JSON body.
3. That `Expose` really is required for block visibility in 18.1.1. The package's comment
   says so; a save-and-inspect confirms it.
4. That six consent posts stay inside the throttle when the scan runs quickly.

## Limitations, stated deliberately

**The TempData cookie may not be observed.** `.AspNetCore.Mvc.CookieTempDataProvider` is set
only by a request that writes `TempData`, which here means a booking, cancel,
child-management or registration POST. `LoginSurfaceController` reports failures through
`ModelState`, so a wrong-password login does not trigger it either. Rather than have the
scanner submit booking forms, the catalogue knows this cookie and the report lists it under
"expected but not observed" for a deliberate manual decision.

**No pixel detection.** Third-party hosts contacted are reported; no `Pixel` declaration is
inferred from them.

**A scan is a snapshot.** It is true on the day it runs. Keeping it true means running it,
ideally on a schedule, gating on exit code `1`.

## Why the six passes, given the site has no trackers today

The first scan's yield is first-party framework cookies, not trackers — with the TempData gap
as the concrete known miss. That is not an argument for cutting the per-category passes down
to three. The passes cost nothing while there is nothing to categorise, and they are what
makes the first analytics tag anyone adds categorise itself correctly instead of arriving as
a guess that needs a human to check.
