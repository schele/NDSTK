# Cookie consent and cookie policy — design

**Date:** 2026-08-21
**Status:** Approved (design), not yet implemented
**Context:** Replaces a Cookietractor subscription (2 500 kr/year ex. VAT, cheapest tier) with a
self-hosted consent management platform for the NDSTK Umbraco 18 site.

## Why

The site needs a consent mechanism and a cookie policy page. A commercial CMP was rejected on
recurring cost. The decision to build the full feature set — rather than the smaller solution the
current site would justify — was made deliberately after being shown the trade-off.

### Baseline audit (2026-08-21)

The public site currently sets **no cookies at all**. Verified by reading `Set-Cookie` headers:

| Route | Status | Cookies set |
| --- | --- | --- |
| `/` | 200 | none |
| `/articles/new-season-kickoff-event/` | 200 | none |
| `/logga-in/` | 200 | none |
| `/nope/` (404 → Error page) | 404 | none |
| `/umbraco` | 200 | none on the SPA shell |

Project code contributes no `<script>` tags, no external hosts and no client-side storage. The only
inline JS is the language selector's `onchange`, which is currently not rendered.

Consequence: nothing on the site requires consent today. The consent machinery is therefore built as
*infrastructure that stays dormant* until a cookie needing consent is introduced — most likely the
BankID member auth cookie (which is strictly necessary and needs disclosure, not consent) or a future
analytics or embed script.

## Deliberate deviations from Cookietractor

Recorded here so they are visible rather than discovered later.

| Deviation | Reason |
| --- | --- |
| First-run UI is a **bar, not a modal** | Compliance requires that non-necessary cookies are not *set* before a choice, which is guaranteed server-side. Blocking the page is not required, and a page-blocking modal is markedly more hostile to keyboard and screen-reader users. |
| **No dark mode** | The site is a committed light design with fixed colours. A dark banner on a light site reads as broken. |
| **Drift detector instead of a cookie crawler** | Real scanning needs a headless-browser crawler plus a shared cookie database across many customers. For one domain, diffing observed cookie names against the declared registry gives the same actionable output for a fraction of the work. |
| **No IP address or user agent in the consent log** | Demonstrating consent requires showing that a consent with given parameters was recorded and that the visitor's cookie carries the matching id. It does not require identifying the person. Logging IPs would make the consent log itself personal data, requiring its own lawful basis and retention justification. |

## 1. Categories

Four categories. Consent is granted per category, not per cookie — per-cookie granularity is rarely
used and makes an accessible banner substantially harder.

| Category | Consent required | Examples on this site |
| --- | --- | --- |
| `necessary` | No — always on, not declinable | Umbraco antiforgery, BankID member auth, the consent cookie itself |
| `preferences` | Yes | Language choice, remembered UI state |
| `statistics` | Yes | Analytics, if added |
| `marketing` | Yes | YouTube/Vimeo embeds, social, ad tech |

## 2. Consent cookie

Name `ndstk-consent`. First-party, `Path=/`, `SameSite=Lax`, `Secure` under HTTPS, **not**
`HttpOnly` — client-side script must read it to unblock gated scripts. Twelve-month lifetime.

Value is compact JSON, URL-encoded:

```json
{"v":1,"t":"2026-08-21T09:12:33Z","c":["preferences","statistics"],"id":"9f3aK2p"}
```

| Field | Meaning |
| --- | --- |
| `v` | Policy text version the visitor was shown |
| `t` | Decision timestamp (UTC, ISO 8601) |
| `c` | Granted categories; `necessary` is implied and omitted |
| `id` | Random 128-bit consent id (base64url), links to the server log row |

**The cookie is written by the server**, not by JavaScript. That guarantees the attributes are
correct, and the endpoint returns the canonical state so the banner can unblock scripts without a
page reload.

**Re-prompting** is driven by `v`, sourced from configuration (`Ndstk:Consent:PolicyVersion`) so that
bumping it is a deploy-time decision rather than a code change. Stored `v` lower than current `v`
causes the banner to reappear with the previous choice pre-selected.

Parsing must be defensive: a malformed or truncated cookie is treated as "no decision", never as an
exception. The cookie is not a security boundary — the worst a visitor can do is forge their own
consent — so it is not signed.

## 3. Consent log

Table `ndstkConsentLog`, created by an Umbraco migration.

| Column | Type | Purpose |
| --- | --- | --- |
| `id` | int identity | Primary key |
| `consentId` | nvarchar(32), indexed | Matches the cookie's `id` |
| `createdUtc` | datetime | When the decision was recorded |
| `policyVersion` | int | Which text version was shown |
| `categories` | nvarchar(255) | Granted categories, comma-separated |
| `action` | nvarchar(20) | `accept-all` \| `reject-all` \| `custom` \| `withdrawn` |
| `culture` | nvarchar(10) | Language the text was shown in |

No IP, no user agent, no member id — see deviations above.

**Retention:** 26 months, enforced by a recurring background job. Proof must outlive the 12-month
cookie with margin.

**Endpoint:** `POST /api/consent`. Accepts categories, action and culture; writes one log row, sets the
cookie, returns the canonical consent state. Rate-limited to 10 requests per minute per IP via ASP.NET
Core's fixed-window rate limiter — enough for a visitor changing their mind repeatedly, low enough that
the table cannot be flooded. The limiter reads the IP but does not persist it.

**Staging correction.** The endpoint itself belongs to build-order stages 1–6, not stage 7: the banner
cannot function without it, because §2 requires the server to write the cookie. Stages 1–6 therefore
ship the endpoint setting the cookie only, validating `action` but discarding it. Stage 7 adds log-row
writing inside that existing endpoint.

## 4. Gating contract

Four mechanisms, because different scripts need different treatment.

```razor
@* 1. Known at render time — emits nothing at all when not consented *@
<consent-script category="statistics" src="https://…/gtag.js" async></consent-script>

@* 2. Arbitrary Razor branching *@
@if (Consent.HasGranted(ConsentCategory.Marketing)) { … }
```

```html
<!-- 3. Activate on consent without a reload. Inert by spec: browsers do not execute unknown types -->
<script type="text/plain" data-consent-category="marketing" data-src="…"></script>
```

```razor
@* 4. Embeds — renders a styled placeholder with a "Visa innehåll" button until granted *@
<consent-embed category="marketing" src="@youtubeUrl" title="…" />
```

The "no consenting cookies before a choice" guarantee rests on mechanism 1 being server-side: the tag
never reaches the browser, so there is no race to lose.

## 5. Google Consent Mode v2

`gtag('consent','default', …)` with all signals denied and `wait_for_update: 500`, emitted in `<head>`
ahead of any GTM snippet; `gtag('consent','update', …)` on grant. Emitted **only** when a measurement
id is configured, so it is not dead weight on every page of a site with no analytics.

## 6. Cookie registry

The registry lives on the **cookie policy document type**, not on Settings: the page that publishes
the table owns the data. The banner reads it from the published cache, which is in-memory and
therefore free per request.

Settings gains one property: `cookiePolicyPage` (content picker), so the banner knows where to link —
same pattern as the existing `loginPage`.

**Element type `cookieDefinition`:**

| Property | Data type | Mandatory |
| --- | --- | --- |
| `cookieName` | Textstring | Yes |
| `provider` | Textstring | No |
| `category` | Dropdown (4 categories) | Yes |
| `purpose` | Textarea | Yes |
| `duration` | Textstring | No |
| `storageType` | Dropdown (Cookie / localStorage / sessionStorage / pixel) | No |

New data types: `NDSTK - Cookie category`, `NDSTK - Storage type`, and `NDSTK - Cookie registry`
(Block List admitting only `cookieDefinition`). All created by the existing installer using the
established create-if-missing pattern.

## 7. Policy page

**Document type `cookiePolicy`** — composed with `base` for SEO, template `CookiePolicy`, allowed
under `start`. Properties: `heading`, `introduction` (rich text), `cookies` (registry Block List),
`outro` (rich text).

Rendered structure:

1. H1 and intro prose.
2. **"Dina inställningar"** — current consent state in words, a button to reopen the banner, and a
   withdraw action. Not cosmetic: withdrawal must be as easy as granting.
3. One section per category in fixed order (necessary → preferences → statistics → marketing), each
   with a heading, a standard description from Dictionary, and a table of that category's cookies.
   Columns: *Namn · Leverantör · Syfte · Lagringstid · Typ*.
4. Outro — contact, how to manage cookies in the browser, link to IMY.

Categories with no declared cookies are skipped, so the page stays honest as the site grows.

**Constraint:** this prose is single-language until the document types become culture-variant. Banner
strings are unaffected because Dictionary items are culture-variant regardless of document type
variance.

## 8. Drift detector

**The reporter runs only for authenticated backoffice users browsing the front end, plus always in
Development.** A client-side reporter posting from public visitors' browsers would itself be
processing requiring justification — awkward for a compliance tool. Restricting it to editors and
developers gives the same detection quality with no visitor-side processing.

- **Server half** — middleware recording distinct cookie *names* from outgoing `Set-Cookie` headers.
  Names only, never values.
- **Client half** — reads `document.cookie` names and `localStorage` keys, posts new ones to
  `POST /api/consent/observed`.
- **Storage** — table `ndstkObservedCookie` (name, source, firstSeenUtc, lastSeenUtc), so observations
  survive restarts.
- **Dashboard** — backoffice dashboard with two lists: *Odeklarerade* (observed but not declared) and
  *Deklarerade men inte observerade* (declared but never seen). The second catches stale policies.

## 9. Dictionary items

Banner and category text as Umbraco Dictionary items under a `Cookies.` prefix, seeded with `sv` and
`en-GB` values. Approximately 20 keys: banner heading, body and buttons; four category names and
descriptions; embed-placeholder text; policy-page controls; table headers.

`IDictionaryItemService`'s v18 shape must be verified before relying on it.

## 10. Banner

**Delivery.** Self-hosted `wwwroot/static/js/consent.js` and `consent.css`. Vanilla JS, no
dependencies, no CDN — a consent tool making a third-party request would undercut its own purpose.
Target under 8 KB.

**Rendering.** The markup is real HTML in a Razor partial, not JS-generated. The server already knows
the consent state, so it decides whether the first-run bar starts visible; JS only wires up behaviour.
This removes the flash of a late-appearing banner. One partial renders both the bar and the settings
dialog, so reopening costs no network round trip.

**Accessibility.**

- The first-run element is a **bar**, sitting early in DOM order as `role="region"` with an accessible
  name. It does **not** steal focus on load.
- The settings panel is a native `<dialog>` opened with `showModal()`. Focus containment, Tab cycling,
  Esc-to-close and an inert backdrop come from the platform, correctly. Hand-rolled focus traps are a
  common source of subtle breakage.
- Each category is a `<fieldset>` with a real checkbox and `<label>`, its description, and a
  `<details>` listing that category's cookies for inspection without navigating away.
- The `necessary` checkbox is `checked disabled` **with the reason stated adjacent** — a greyed-out
  box with no explanation is a dead end.
- **Accept and reject have equal visual weight**: same size, same padding, differing only in colour.
  A prominent accept button beside a grey text-link reject is a documented dark pattern.
- `prefers-reduced-motion` disables the slide-in.

**Theming.** The existing palette is already AAA-capable, so no new colours are needed:
`--primary` `#001F54` on white is ≈15.7:1; `--accent` `#F7E300` on `--primary` is ≈13:1. A
`.btn-secondary` is added, matching `.btn-primary`'s box metrics exactly.

**Reopen and withdraw.** A real `<button>` in the footer reading "Cookieinställningar"; any element
with `data-consent-open` also works, as does the policy page's settings section. Withdrawing posts
`action: "withdrawn"` and then reloads — server-emitted scripts must disappear.

**Public API.**

```js
window.ndstkConsent = { open(), get(), has('statistics'), onChange(fn) }
```

Plus a `ndstk:consent-change` DOM event, so future scripts can react without coupling to this API.

## 11. Seeding

The seeder creates the policy page under Start, wires Settings' `cookiePolicyPage` to it, seeds the
Dictionary items in `sv` and `en-GB`, and sets policy version 1.

The registry is pre-filled with what the site actually has — `ndstk-consent`, Umbraco's antiforgery
cookie, and the BankID member auth cookie once it exists, all `necessary`. An honest table for this
site today, rather than invented plausible-looking entries.

## 12. Testing

**Unit-testable:** cookie serialise/parse round-trip including malformed input, version comparison and
re-prompt logic, category-set arithmetic, the tag helper's emit-vs-suppress decision, registry →
category grouping.

**Integration-testable:** the endpoint sets the cookie with correct attributes and writes exactly one
log row; the retention job deletes rows past 26 months; the middleware records cookie names.

**Not automatable here:** a real screen-reader pass. A keyboard-only pass and verification of the ARIA
wiring are in scope; driving an actual screen reader is not, and coverage will not be implied.

**The solution has no test project.** Standing one up is in scope. Sequencing decision: a bare xUnit
project covers every build-order stage from 1 to 6 (see §13) and is trivial to create. The fiddly part
— booting Umbraco inside `WebApplicationFactory` against a real database — is needed only by
build-order stage 7, the consent log, so it is deferred to that stage rather than gating the whole
build.

All stage references in this document use the build-order numbering in §13.

## 13. Build order

Reordered so that the legally meaningful part lands first and the riskiest, least valuable part lands
last.

| Order | Stage | Size | Risk |
| --- | --- | --- | --- |
| 1 | Consent core (§1, §2) | Small | Low |
| 2 | Gating contract (§4, §5) | Small–medium | Low |
| 3 | Content model (§6) | Small | Low |
| 4 | Policy page template (§7) | Small | Low |
| 5 | Banner (§10) | Medium | Low |
| 6 | Dictionary seeding (§9), seeding (§11) | Small | Low |
| 7 | Consent log (§3) | Medium | Medium — introduces migration infrastructure |
| 8 | Drift detector and dashboard (§8) | Large | High — introduces a Node/Vite/TypeScript toolchain |

Stages 1–6 constitute a complete, working, compliant CMP. Stage 7 adds proof of consent, which has no
practical effect until a cookie requiring consent exists. Stage 8 is optional and should be decided
only after the rest is working — a calendar reminder to re-check the policy annually may be worth more
than the dashboard for a site that changes rarely.

**Scope of the first implementation plan:** stages 1–6 only. Stage 7 introduces migration
infrastructure and stage 8 introduces a second build system; each warrants its own plan once the
preceding work is in place and reviewed.

## 14. Open items

| Item | Notes |
| --- | --- |
| Culture variance for policy prose | Document types are invariant. Making them culture-variant is a separate, previously-offered migration. |
| `en-GB` is not routable | No domain or culture binding exists, so multilingual output is not end-to-end testable until one is added. |
| `IDictionaryItemService` v18 shape | Verify by reflection before use, as with the rest of the API surface. |
| Migration infrastructure | The existing installer uses services, not `MigrationPlan`. Stage 7 introduces the latter. |
| Maintenance tail | Roughly an hour whenever the site gains a third-party script. The recurring cost does not disappear; it stops being billed. |

## 15. Non-goals

- A cookie crawler.
- Multi-domain or multi-tenant support.
- Consent signalling frameworks beyond Google Consent Mode v2 (e.g. IAB TCF).
- Storing personal data of any kind in the consent log.
