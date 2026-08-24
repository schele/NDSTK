# Member administration, participants and family accounts — design

Date: 2026-08-25
Branch: `feature/member-booking`
Target: NDSTK, Umbraco 18.1.1 on .NET 10, SQLite
Follows: `2026-08-24-member-booking-design.md` (all seven phases complete)

## Purpose

Two things the club cannot do today.

**Administer members.** There is no backoffice surface at all. Whether someone has paid,
how much, when they joined, when they confirmed their email, how long their membership has
left, what they have booked — all of it is readable only by opening the SQLite file. The
club needs those facts in the backoffice, and it needs to see who is booked into each
class.

**Take a booking for a child.** The booking tables are keyed by `MemberKey`, so one account
is exactly one participant. A parent with two children needs two email addresses, and the
partial unique index on `(MemberKey, ClassKey)` physically prevents putting two siblings in
the same Tuesday group. This introduces participants: the account holder is a guardian, and
the people who attend are named children hanging off the account.

## Scope

In scope:

- A `ndstkParticipant` table; every booking belongs to a participant, not to an account.
- Registration collects the child's name and birth date, and the guardian's name and phone.
- A paid **family account** upgrade allowing several children on one account.
- A `familjekonto` flag and a `familjetillägg` price, both editable in the backoffice.
- The welcome price becomes once per child rather than once per account.
- A **Medlemmar** dashboard in the Members section: one row per account, searchable, CSV export.
- A **Deltagare** workspace view on the `trainingClass` node: the roster for that class.
- A migration that backfills every existing member and booking.

Out of scope, and why:

- **Attendance marking.** Decided against: no present/absent toggle, so a true no-show is
  not recorded. See *What "missade" means* below for what is shown instead, and for the
  one nullable column that would add it later without a remodel.
- **Sibling dues at a discounted rate.** Superseded by the family account: one årsavgift
  covers everyone on the account, so there is no "which child holds the full slot" rule to
  get wrong.
- **A fixed shared season.** Memberships stay rolling 365 days from payment, as built.
- **Editing participants from the backoffice.** The dashboard is read-only. Members manage
  their own children in Mina sidor; an admin correcting a birth date is a later request.
- **Per-child membership expiry.** One date per account, on the member, as today.

## Decisions taken during brainstorming

| Question | Decision |
| --- | --- |
| Who attends? | The account holder is a guardian. Named participants attend. |
| Årsavgift per child or per family? | Per family, via the paid family upgrade. |
| What does the 100 kr buy? | One årsavgift covers every child on the account. |
| Does the upgrade move the expiry date? | No. One date; the upgrade sets a flag only. |
| Renewal price | `membershipFee` solo, `membershipFee + familyFee` for a family account. |
| Prices | All three editable on the Settings node, in kronor. |
| Welcome price | Once per **child**, not once per account. |
| Missed classes | Inferred. No attendance marking. |
| Solo accounts | Still name a child, with a birth date. No implicit self-participant. |

## Verified platform facts

Checked against the 18.1.1 assemblies and against a working package in
`c:\src\Esatto.Packages`, not read from documentation.

- `AuthorizationPolicies.SectionAccessMembers` exists in `Umbraco.Web.Common.dll`, so the
  management API can be gated on "can see the Members section".
- The backoffice section alias is **`Umb.Section.Members`** (plural), confirmed in
  `Umbraco.Cms.StaticAssets` 18.1.1. `Umb.Section.Member` does not exist.
- **`Umb.Condition.WorkspaceContentTypeAlias`** exists, which is what scopes a workspace
  view to the `trainingClass` document type.
- `EmailConfirmedDate` is present in `Umbraco.Core.dll` 18.1.1. Its declaring type must be
  confirmed at implementation; if it is not reachable from `IMember`, the fallback is a
  `verifieradUtc` member property stamped by `MemberVerifyController` on confirmation.
- **A backoffice extension needs no npm and no bundler.**
  `Esatto.Umbraco.Backoffice.Redirects` ships a hand-written `umbraco-package.json` beside
  a plain ES-module Lit element in `wwwroot/App_Plugins/`, importing from
  `@umbraco-cms/backoffice/*` via the backoffice import map at runtime. This repo has no
  node build today and will not need one.
- Management API calls from that element must use `umbHttpClient` with
  `security: [{ scheme: 'bearer', type: 'http' }]` declared explicitly. Without it no
  bearer token is attached and every request 401s.
- The existing partial unique index is raw SQL in `AddBookingTables`, because Umbraco's
  expression builder has no partial-index support. The replacement will be too.

## Architecture

### The participant table

```
ndstkParticipant
  Id                int      PK, autoincrement
  Key               Guid     unique  — what bookings reference
  MemberKey         Guid     indexed — the guardian's account
  FirstName         string(100)
  LastName          string(100)
  BirthDate         DateTime?         — nullable only for backfilled rows
  FirstClassUsedUtc DateTime?         — the welcome price, moved off the member
  CreatedUtc        DateTime
  RemovedUtc        DateTime?         — soft delete; bookings must stay readable
```

`BirthDate` is nullable in the schema and required in the form. The only rows that may
carry a null are the ones the migration backfills for members who registered before this
existed; the portal blocks booking until such a row is completed. Making the column
non-null instead would mean inventing a birth date for those members, which is worse than
asking them.

Removal is soft. Deleting the row would orphan the child's booking history and quietly
change last season's class numbers.

### The change to `ndstkBooking`

One new column and one index swap:

```sql
ALTER TABLE ndstkBooking ADD COLUMN ParticipantKey TEXT NULL;

DROP INDEX IX_ndstkBooking_OneLivePerMemberClass;          -- (MemberKey, ClassKey)

CREATE UNIQUE INDEX IX_ndstkBooking_OneLivePerParticipantClass
ON ndstkBooking (ParticipantKey, ClassKey)
WHERE Status IN ('Pending', 'Confirmed');
```

This index swap is the whole family feature. Under the old index a guardian cannot book
two siblings into the same class — the second `INSERT` trips the constraint and
`TryReservePlaceAsync` returns null, which `BookingService` reports as *already booked*.
Under the new one, two siblings are two participants and both fit, while one child still
cannot be booked onto the same class twice.

`MemberKey` **stays on the booking row**. It is who pays, and the payment, credit and
reminder queries all key off it. Dropping it in favour of a join would put a join on the
reminder job's hot path for no gain.

### Membership, and the family flag

The membership clock does not move. `membershipPaidUntil` stays a member property: one
date per account, covering every participant on it.

New member type properties, both administrative — a member may see them, and a member who
could edit them would have a free membership, so both are `canView: true, canEdit: false`
in the same call `NdstkContentModelInstaller` already makes:

| Alias | Type | Meaning |
| --- | --- | --- |
| `familjekonto` | True/false | The account may hold more than one participant. |
| `telefon` | Textstring | Guardian's phone, shown on the class roster. |

`firstClassDiscountUsed` is **retired** from the member type. It is not deleted — an
existing site has values in it that the migration reads — but nothing writes it after the
migration, and `MemberProfileService` stops exposing it.

### Settings additions

One new field in the existing *Medlemskap* group, entered in kronor like its neighbours
and converted to öre once in `MembershipSettingsService`:

| Field | Default |
| --- | --- |
| Familjetillägg (kr) | 100 |

A zero still counts as "not set" and falls back to the default, per field, as the existing
fields do.

### `PaymentRecord` gains a column

```
FamilyFeeOre  int
```

`AmountOre` is already the total and `MembershipFeeOre` / `ClassFeeOre` already split it.
Adding the family part keeps the split complete, which is what lets the admin view answer
"how much, and for what" without inferring anything from the total.

No `ParticipantKey` on the payment: it is reachable through `BookingId`, and payment
settlement happens once per booking, so the extra read is not worth denormalising a second
time.

### Credits stay on the account

A credit is issued to `MemberKey` and is spendable for **any** participant on that account.
The account paid for the cancelled class, so the account keeps the place. The alternative —
tying a credit to the child whose class was cancelled — is defensible but means a family
can hold a credit it cannot use when that child stops training.

## Pricing

The rules stay pure functions in `NDSTK.Domain`. The signatures change:

```csharp
// was: Quote(MemberState member, PriceList prices, bool useCredit, DateOnly today)
BookingQuote Quote(
    MemberState member, ParticipantState participant,
    PriceList prices, bool useCredit, DateOnly today);

record MemberState(DateOnly? MembershipPaidUntil, bool IsFamilyAccount);
record ParticipantState(bool FirstClassUsed);
record PriceList(int MembershipFeeOre, int FamilyFeeOre, int FirstClassPriceOre, int ClassPriceOre);
record BookingQuote(int MembershipDueOre, int FamilyDueOre, int ClassFeeOre);
```

The rules, in full:

- **Membership due.** Zero while `membershipPaidUntil >= today`, inclusive of the last day.
  Otherwise `membershipFee`, plus `familyFee` when `familjekonto` is set. Paying it moves
  the date to `today + 365`.
- **Family due on a booking.** Only as part of a lapsed renewal, never on its own. A member
  who is paid up and upgrades mid-year buys it separately (below).
- **Class fee.** `firstClassPrice` while that **child's** `FirstClassUsedUtc` is null, then
  `classPrice`. Once per child, for life — a child returning after a lapse pays full price.
- **Credit.** Clears the class fee. Never clears the membership or family fee.

### The mid-year family upgrade

A separate purchase, not part of any booking. It creates a `PaymentRecord` with
`BookingId = null`, `MembershipFeeOre = 0`, `ClassFeeOre = 0`, `FamilyFeeOre = familyFee`,
and routes through the existing mocked Swish page. On settlement it sets `familjekonto` and
**does not touch `membershipPaidUntil`**.

Leaving the date alone is the point. If the 100 kr moved the date forward a year it would
be a cheaper renewal than the 150 kr årsavgift, and no member would ever pay the årsavgift
twice. The trade is that upgrading a month before expiry buys only that month — visible,
honest, and self-correcting, since the member renews at the family price next time.

`SettlePaymentAsync` already handles `BookingId is null`, so this needs a branch, not a
second settlement path.

## Migration and backfill

Two stages, and the order is load-bearing.

**Stage one — the migration**, schema only, added to `BookingMigrationPlan`:

1. `CREATE TABLE ndstkParticipant`.
2. `ALTER TABLE ndstkBooking ADD COLUMN ParticipantKey`.
3. `ALTER TABLE ndstkPayment ADD COLUMN FamilyFeeOre` (default 0).

**Stage two — `NdstkParticipantBackfill`**, a notification handler that runs after the
migration plan completes, guarded by a marker in the key/value store the way
`NdstkMemberContentUpgrade` is. It needs `IMemberService`, which a migration should not
reach for:

4. For every existing member: insert one participant, `FirstName` from the email's local
   part, `LastName` empty, `BirthDate` null.
5. `UPDATE ndstkBooking SET ParticipantKey = ...` joining on `MemberKey`.
6. Where the member's old `firstClassDiscountUsed` was true, stamp that participant's
   `FirstClassUsedUtc` from their earliest completed payment, falling back to the member's
   create date.
7. Drop the old index, create the new one.

**Step 7 belongs in stage two, after step 5, not in the migration.** Creating the unique
index while `ParticipantKey` is still null on every row does **not** fail — SQLite treats
nulls as distinct in a unique index — it silently produces an index that enforces nothing,
and the overbooking guarantee the previous phase verified with 60 concurrent attempts would
be gone without any error being raised.

Leaving the old `(MemberKey, ClassKey)` index in place across stage one is deliberate: the
site is not serving traffic between the two stages, but if it ever were, the old index
still enforces the old guarantee right up until the new one replaces it. At no point is
there a window with no index at all.

## Flows

### Registration

`RegisterFormModel` gains six required fields:

| Field | Validation |
| --- | --- |
| Ditt förnamn / efternamn | Required. Becomes the member's `Name`. |
| Telefon | Required. Shown on the class roster. |
| Barnets förnamn / efternamn | Required. |
| Barnets födelsedatum | Exactly 8 digits, `ÅÅÅÅMMDD`, and a real date. |

The birth date is entered and displayed as `ÅÅÅÅMMDD` throughout — the Swedish convention,
and the first eight digits of a personnummer, which is what a parent will type without
being asked. It is stored as a date. **No personnummer is collected or stored.**

The participant row is written only when `memberManager.CreateAsync` succeeds, keyed by
`user.Key`, before the verification mail is sent. A duplicate address still returns the
same "check your inbox" response and writes nothing, so registration stays
enumeration-resistant.

Guardian name replaces the email as the member's display name, because a list of email
addresses is not something anyone can administer.

### Mina barn

A section of the portal listing the account's participants: name, birth date, age, and
their booking count. A solo account shows one and can only edit it. A family account can
add and remove.

Adding a child on a solo account is refused with an offer to upgrade. Removing sets
`RemovedUtc`; the child's history stays.

A participant with a null birth date — only ever a backfilled one — is shown with a prompt
to complete it, and booking for that child is refused until it is filled in.

### Booking

The class list gains a child picker when the account has more than one live participant,
and books silently for the only one when it does not. The quote is per child: two siblings
on the same class are two bookings, two class fees, and at most one membership fee.

## The backoffice

Both surfaces are plain ES-module Lit elements in
`wwwroot/App_Plugins/NDSTK.MemberAdmin/`, declared in one hand-written
`umbraco-package.json`. No npm, no bundler, no build step.

One server-side controller, `MemberAdminController`, deriving from
`ManagementApiControllerBase` with `[VersionedApiBackOfficeRoute("backoffice/ndstk/members")]`
and `[Authorize(Policy = AuthorizationPolicies.SectionAccessMembers)]`. Its read model is a
new `MemberAdminQueries` service — SQL only, no writes, kept out of `IBookingRepository`
so the booking path's interface does not grow a reporting surface.

### Dashboard: Medlemmar

Conditioned on `Umb.Section.Members`. One row per account:

| Column | Source |
| --- | --- |
| Namn / e-post | Member `Name`, `Email` |
| Fam | `familjekonto` |
| Verifierad | `EmailConfirmedDate`, or the fallback property |
| Medlem sedan | Earliest completed payment including a membership fee; else member create date |
| Går ut | `membershipPaidUntil` |
| Kvar | `membershipPaidUntil − today`, negative shown as *Utgången* |
| Deltagare | Count of participants where `RemovedUtc is null` |
| Betalt | Sum of `AmountOre` where status is Paid |
| Senaste betalning | Latest `CompletedUtc` |
| Bokade | Confirmed bookings |
| Avbokade | Cancelled bookings |
| Ej betalda | Bookings expired on a payment hold |
| Krediter | Unspent credits |

Free-text search over name, email and child name. CSV export of exactly the visible rows —
for a club this is the feature that gets used most.

Clicking a row opens a detail panel: every payment with its full split (årsavgift /
familjetillägg / klassavgift / total / status / date), and every booking grouped by child.

### Workspace view: Deltagare

Conditioned on `Umb.Condition.WorkspaceContentTypeAlias` matching `trainingClass`, so it
appears as a tab when an editor opens a class in Content — beside the fields, where they
already are. It lists each booked child with age, the guardian's name, email and phone, and
the booking status, above an *X av Y platser bokade* count.

Putting the roster on the class node rather than on a separate screen means it cannot drift
out of sync with the class being edited, and it needs no navigation of its own.

A cross-class overview — every class this week with its booked-versus-capacity — reuses the
same endpoint grouped differently. Deferred: the workspace view answers the stated need,
and the overview can be added without changing anything server-side.

### What "missade" means

Attendance is not recorded, so the dashboard shows **Avbokade** (cancelled, credit issued)
and **Ej betalda** (hold expired, place released). A child who booked, never cancelled and
never turned up is indistinguishable from one who attended, and contributes to neither
column.

This is recoverable. Adding a nullable `AttendedUtc` to `ndstkBooking` and a toggle to the
Deltagare view is the whole change; nothing in this design has to move.

## Security and data protection

- The management API is gated on `SectionAccessMembers`, so backoffice authorisation is
  Umbraco's, not a check of ours.
- Participants are minors' names and birth dates. They live in a database table, never in
  the published cache, and are never rendered on a public page. This is why participants
  are not content nodes.
- No personnummer. The birth date is eight digits and stops there.
- Deleting a member cascades: their participants are removed and their bookings anonymised
  rather than deleted, so historical class numbers stay correct.
- Registration keeps its existing protections unchanged — honeypot, render timestamp,
  per-IP rate limiting, and the password-error-before-duplicate-address ordering that keeps
  the response leak-free. The new fields are validated after that ordering, not before it.
- Every portal write verifies the participant belongs to the signed-in member, the way the
  Swish endpoints already verify payment ownership.

## Testing

`NDSTK.Domain` stays free of Umbraco and the database, so the rules stay cheap to test:

- Pricing with a family account, lapsed and current.
- The welcome price per child: three children on one account each get one.
- A credit clearing the class fee but never the membership or family fee.
- The family upgrade quoted on its own, with the date untouched.
- Capacity and `HasLiveBooking` keyed by participant: two siblings fit one class, one child
  cannot be booked onto it twice.

The migration backfill cannot be unit-tested — the test project deliberately does not
reference the web assembly — so it is verified at runtime against a copy of the current
database, the way the previous phases were verified. `umbraco/Data/Umbraco.sqlite.db` has a
backup beside it already.

## Implementation phases

| Phase | Scope |
| --- | --- |
| 8 | `ndstkParticipant`, the index swap, the migration and backfill, pricing rules and their tests |
| 9 | Registration fields, Mina barn, the child picker on booking, the family upgrade purchase |
| 10 | The management API, the Medlemmar dashboard, the Deltagare workspace view |

The backoffice comes last deliberately. Its most valuable columns are per-child, so
building it against today's model means building it twice.

## Assumptions

- **Guardian name and phone are required at registration.** Neither exists today. The
  admin list and the class roster are both unusable without them, and asking two more
  questions at registration is cheaper than chasing the answers later.
- **The family upgrade has no child limit.** No cap on participants per account. If one
  account is ever shared across a neighbourhood, a `familjeMaxDeltagare` setting is one
  field and one check.
- **An adult who trains themselves** is a participant on their own account, entering their
  own name and birth date. Nothing special is needed for this case.
- **Existing test members** are backfilled with a placeholder name from their email and a
  null birth date, and are prompted to complete it before their next booking.
