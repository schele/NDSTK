# NDSTK

Umbraco 18 site for Norra Djurgårdsstadens Tennisklubb, with a member area for booking training
classes.

- Umbraco CMS 18.1.1 on .NET 10, SQLite
- `dotnet run` — https://localhost:44351
- `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — the booking rules

## Projects

| Project | Contains |
| --- | --- |
| `NDSTK` | The site: content model, views, controllers, data access |
| `NDSTK.Domain` | The booking rules as pure functions — no Umbraco, no database |
| `NDSTK.Tests` | xUnit over `NDSTK.Domain` |

`NDSTK.Domain` exists so those rules cannot acquire a dependency on Umbraco by accident, and so the
test suite does not build the web assembly — a running site holds a file lock on it, and tests you
have to stop the site to run are tests you stop running.

**`NDSTK.csproj` sits at the repository root**, so any new sibling project must be added to its
`DefaultItemExcludes`. Otherwise the SDK's default globs pull that project's sources into the web
assembly and the build fails with duplicate assembly attributes.

While the site is running, `dotnet build` cannot overwrite the output. Use

```
dotnet build -t:"ResolveReferences;CoreCompile"
```

to type-check without linking. **`-t:CoreCompile` alone is not enough** once you have touched
`NDSTK.Domain`: without `ResolveReferences` the compiler runs with no reference assemblies at all
and buries the real errors under a hundred lines of "predefined type `System.Object` is not
defined". It appears to work only while that project is already up to date.

## The content model is code-first

`ContentModel/` declares document types, templates and data types, and creates whatever is missing on
every boot. Do not hand-create these in the backoffice — add them to the installer so a fresh
database ends up identical.

Three distinct mechanisms, and the difference matters:

| Class | Behaviour |
| --- | --- |
| `NdstkContentModelInstaller` | Create-if-missing. Never touches a type that already exists, so backoffice edits survive a restart. |
| `NdstkContentTypeFactory.EnsureGroupAsync` / `EnsureMemberPropertiesAsync` | Adds *missing fields* to a type that already exists. The only way to roll a new field out to a live site. |
| `NdstkMemberContentUpgrade` | Overwrites content an editor could also have changed, so it is guarded by a marker in the key/value store and runs exactly once. Bump `StateValue` to make it run again. |

`NdstkContentSeeder` fills a brand-new site and does nothing once the tree has any content.
`NdstkPageInstaller` creates individual pages by key on every boot, which is what reaches a site that
is already live.

## Member booking

Register → confirm the emailed link → sign in → book a class for one of your children → pay
through the mocked Swish page.

**The account holder is a guardian; the people who attend are participants.** Even a solo account
names one child, with a birth date — there is no such thing as an account that books for itself
implicitly. Registration collects the guardian's name, phone and email, and the first child's name
and birth date.

Prices live on the **Settings** node under *Medlemskap*, in kronor:

| Field | Default |
| --- | --- |
| Årsavgift | 150 |
| Familjetillägg | 100 |
| Pris första klassen | 100 |
| Pris per klass | 200 |
| Påminnelse (timmar innan) | 24 |
| Betalningsreservation (minuter) | 5 |

Money is stored as **integer öre** throughout; the ×100 happens once, in
`MembershipSettingsService`. SQLite maps `decimal` to `REAL`, and floating point has no place in a
payment record.

**A zero counts as "not set", per field.** An empty price field is far likelier than a deliberate
giveaway, so each falls back to its default independently.

### Pricing rules

- The annual fee is charged on the first booking, and again on the first booking after it lapses.
  Membership runs 365 days from the day the payment completes. **One expiry date per account**,
  covering every child on it.
- A **family account** may hold more than one child. It costs the annual fee *plus* the
  familjetillägg, so renewal is 150 or 250 depending on one flag.
- The 100 kr welcome price is **once per child, for life** — not once per account. A second child
  on a family account gets their own trial class; a lapsed member renewing pays 150 + 200.
- Cancelling gives no refund. It issues one **credit**, worth one place on any class with room.
  Credits belong to the account, so any child can spend one.
- Spending a credit costs nothing, so a paid-up member skips the payment step entirely. A lapsed
  member spending a credit still pays the annual fee.

### Family accounts

A solo account may have exactly one child. Upgrading is a purchase of its own from *Mina barn*:
100 kr, no booking attached, and on settlement it sets `familjekonto` and **leaves
`membershipPaidUntil` alone**.

Leaving the date alone is the whole design. If the supplement moved the expiry a year forward it
would be a cheaper renewal than the annual fee, and nobody would ever pay the annual fee twice.
`SettlePaymentAsync` only extends the membership when `MembershipFeeOre > 0`, which an upgrade
payment sets to zero — that one guard is what enforces it.

Birth dates are entered and shown as **ÅÅÅÅMMDD**, the first eight digits of a personnummer,
because that is what a parent types without being asked. A twelve-digit value is rejected rather
than truncated: **no personnummer is collected or stored.**

### Classes

Content nodes under **Träningar**. `capacity` is the maximum number of participants and appears to
members as "8 av 8 platser kvar". `start` is entered as **Swedish local time** and converted to UTC
on the way into the booking tables — `SwedishTime` is the only place that conversion happens.

Three example classes are seeded on a fresh site. Delete them; they are create-once by key and will
not come back.

Moving, unpublishing or deleting a class is handled: `TrainingClassChangedHandler` repoints existing
bookings at the new time, or cancels them and issues a credit each if the class is withdrawn.

### Tables

`ndstkBooking`, `ndstkPayment`, `ndstkBookingCredit`, created by `AddBookingTables`.
`ndstkParticipant` — the children — created by `AddParticipantTable`.

Participants are a table rather than Umbraco members because Umbraco requires a unique email per
member: three siblings would mean three synthesised addresses and three Identity logins to disable.
They are a table rather than content nodes because they are minors' names and birth dates, and
those have no business in the published cache. Removal is **soft**, so a departed child's bookings
still have a name against them.

Credits are a **ledger, not a counter** — spending one is a conditional `UPDATE`, which cannot
double-spend, and the rows are an audit trail.

Reserving a place is a **single conditional `INSERT`**, so two members clicking at the same moment
cannot both take the last place. Verified with 60 concurrent attempts at a capacity-8 class. Note
that SQLite serialises writers, which helps this hold; on SQL Server the same statement would want a
lock hint.

**The one-live-booking index is keyed on the child, not the account** —
`IX_ndstkBooking_OneLivePerParticipantClass` over `(ParticipantKey, ClassKey)`. That single line is
the family feature: under the old `(MemberKey, ClassKey)` index a parent physically could not book
two siblings into the same Tuesday group. It has to stay in step with `Capacity.HasLiveBooking`,
which is the same rule in C#, and with the stale-hold cleanup in `TryReservePlaceAsync` — cleaning
up by account there would retire a *sibling's* live hold.

`NdstkParticipantBackfill` gives pre-participant members one child each, points their bookings at
it and swaps the index. It runs once, guarded by a key/value marker, straight after the migration
plan. **The index swap is last, after every `ParticipantKey` is filled in**: creating a unique index
on a column that is null everywhere does not fail — SQLite treats nulls as distinct — it silently
produces an index that enforces nothing, and the overbooking guarantee would be gone with no error.
The backfill refuses to swap while any booking is unpointed, and only sets its marker if the swap
happened, so a partial run retries on the next boot.

Datetimes are stored as TEXT in NPoco's `yyyy-MM-dd HH:mm:ss.fffffff`. **Do not hand-format a date
into raw SQL** — round-trip `"o"` format sorts differently as text (`T` above space), which silently
breaks the reminder window.

## Backoffice administration

Two surfaces, both in `wwwroot/App_Plugins/NDSTK.MemberAdmin/`:

- **Medlemmar** — a dashboard in the Members section. One row per account: verified date, member
  since, expiry and days left, children, total paid, bookings, cancellations, unpaid holds and
  unspent credits. Search covers child names as well as the parent's, because a coach knows the
  child. Exports the visible rows as CSV.
- **Deltagare** — a workspace view on the `trainingClass` node, so the roster is a tab on the class
  an editor already has open rather than a screen they have to navigate to. Shows each booked child
  with their age *on the class date*, and the guardian's name, email and phone.

**No npm, no bundler, no build step.** A hand-written `umbraco-package.json` beside plain ES
modules that import from `@umbraco-cms/backoffice/*` through the backoffice import map at runtime —
the same pattern as `Esatto.Umbraco.Backoffice.Redirects`. Calls go through `umbHttpClient`, which
**must** be given `security: [{ scheme: 'bearer', type: 'http' }]` explicitly or it attaches no
token and every request 401s.

Server side is `MemberAdminQueries` and `MemberAdminController`, read-only and gated on
`AuthorizationPolicies.SectionAccessMembers`. Kept out of `IBookingRepository` so the booking path's
interface does not grow a reporting surface. The counts come from four grouped queries joined in
memory — two hundred members must not mean two hundred round trips.

### What "missade" can and cannot mean

**Attendance is not recorded.** The dashboard shows *Avbok.* (the member cancelled, and got a
credit) and *Ej bet.* (the payment hold expired and the place was released). A child who booked,
never cancelled and never turned up is indistinguishable from one who attended, and appears in
neither column.

That is a deliberate choice, and it is recoverable: a nullable `AttendedUtc` on `ndstkBooking` and a
toggle on the Deltagare view would be the whole change. Nothing else would have to move.

## Mail

`Umbraco:CMS:Global:Smtp` in `appsettings.json`: one.com's `send.one.com:465`, `SslOnConnect`, from
`info@ndstk.se`.

**The mailbox password is not in the repository.** Add it to `appsettings.Secrets.json`, which is
gitignored and already loaded by `Program.cs`:

```json
{ "Umbraco": { "CMS": { "Global": { "Smtp": { "Password": "…" } } } } }
```

Locally you do not need it. `appsettings.Development.json` sets `SpecifiedPickupDirectory`, so every
message is written to `umbraco/Logs/Mail` as an `.eml` file you can open — the whole registration and
reminder flow is testable without a live mailbox.

**Never set `PickupDirectoryLocation` in `appsettings.json`.** Umbraco's `EmailSender` checks it
*before* SMTP, so in production it would write every message to disk instead of sending it.

## Reminders

`ClassReminderJob` runs every 15 minutes and does two things: sends a reminder for each confirmed
booking starting within the configured window, and releases payment holds nobody completed.

- Guarded by `IServerRoleAccessor`, so only one server in a multi-server deployment sends.
- Each booking is stamped **before** the mail is sent, conditionally, so overlapping runs cannot
  both send. A crash between stamp and send loses that one reminder, which is better than mailing a
  member the same reminder repeatedly.
- The portal's reminder banner is a pure read of the bookings list, so it cannot disagree with the
  emails.

Locally, `appsettings.Development.json` sets the `NDSTK` namespace to `Debug`, so a run that finds
nothing still logs — otherwise "ran and found nothing" is indistinguishable from "never ran".

## Swish is mocked

`SwishMockPaymentProvider` behind `IPaymentProvider`. The payment page is marked **Demoläge** and its
two buttons stand in for the callback a real integration would receive. Replacing it is a second
`IPaymentProvider` and one line in `BookingComposer`.

Both buttons are POSTs with antiforgery, and both verify the payment belongs to the signed-in member.

## Security

- The member portal is protected by Umbraco's **public access** against the *Medlemmar* group, not by
  a check in a controller, so the pipeline turns anonymous visitors away before any of our code runs.
  Verified members are added to that group on confirmation — without it they could sign in and then
  be bounced off their own portal.
- Registration and login are **resistant to account enumeration**: a duplicate address gets the same
  response as success, and a wrong password is indistinguishable from an unknown address. "Activate
  your account" appears only after an explicit password check, because Identity decides
  `IsNotAllowed` before verifying the password.

  The birth-date checks added for the child fields sit **after** the bot guards and **before**
  `CreateAsync`, so this ordering is untouched. A malformed date is true of the value whatever
  address it was paired with, so reporting it reveals nothing.
- Every participant write verifies ownership **inside the `UPDATE`** rather than reading first, so a
  forged key in a POST changes nothing instead of racing a check that passed. Booking re-checks the
  same thing in `BookAsync`: the participant key arrives on a form.
- The backoffice API is gated on `SectionAccessMembers` and has **no write endpoints**. Members
  manage their own children; an administrator editing a birth date would need an audit trail a
  dashboard does not have.
- Participants are minors' names and birth dates. They live in a database table, never in the
  published cache, and are never rendered on a public page. **No personnummer is collected.**
- Per-IP rate limiting on the member forms. `UseRateLimiter()` must stay **inside** the
  `WithMiddleware` callback: registered before `UseUmbraco()` it finds no matched endpoint, no policy,
  and silently permits everything.
- The **verification link expires after 15 minutes** (`MemberVerificationTokenOptions.Lifespan`).
  Registering again resends a fresh link to an account that has not been verified, so a member who
  is too slow is never stuck.

  **Do not shorten this by configuring `DataProtectionTokenProviderOptions`.** That type is one
  unnamed options instance shared by every Identity token provider in the application: both members
  and backoffice users are registered with `AddDefaultTokenProviders()`, so changing it there would
  also cut backoffice user invite and password reset links from a day to fifteen minutes, and a new
  editor would find the link in their invitation mail already dead.

  It is scoped to members instead by giving the token its own options subclass and pointing
  `IdentityOptions.Tokens.EmailConfirmationTokenProvider` at it in `BookingComposer`. That is
  members-only because `MemberManager` takes `IOptions<IdentityOptions>` while `BackOfficeUserManager`
  takes `IOptions<BackOfficeIdentityOptions>` — a derived type, and therefore a separate instance
  that `Configure<IdentityOptions>` never touches.

  Changing `Lifespan` needs no migration, but changing the provider's `Name` does invalidate every
  link already in flight: the name is the data protection purpose string, so tokens issued under the
  old one no longer decrypt.
- Accounts are created unapproved. Umbraco's own sign-in refuses an unapproved member, so an
  unverified account cannot sign in even if the controller check were bypassed.

### Dropping back to one child

Removing the second-to-last child clears `familjekonto`, so the next renewal is the plain årsavgift
again. Left alone the supplement would be charged at every renewal for ever, with nothing in the
portal able to stop it.

Nothing is refunded — the model has no refunds — but nothing is lost either: the supplement already
paid covers the rest of that membership year, so **re-activating inside the same year is free**.
`HasPaidFamilyFeeSinceAsync` establishes that, looking back from the expiry over
`Pricing.MembershipDays`. Without it, remove-then-re-add would bill the supplement twice in one
year, which is exactly the mistake the standalone upgrade used to make.

The button therefore has three states, and it must never quote a price the controller will decline
to charge:

| Account state | Button | Charged |
| --- | --- | --- |
| No valid membership | *Aktivera familjekonto* | 0 now; supplement rides along with the next booking |
| Valid membership, supplement already paid this year | *Aktivera familjekonto igen* | 0 |
| Valid membership, supplement not yet paid | *Uppgradera till familjekonto — 100 kr* | 100 |

### A build trap worth knowing

An **incremental** build can leave the Razor views half-compiled: the build reports success, and
then every front-end route 404s with `No physical template file was found for template …` in the
log while the backoffice keeps working. `dotnet build --no-incremental` clears it. If the site
suddenly serves nothing but `/umbraco`, reach for that before suspecting the content cache.

### Removing a child

Removal is a soft delete: `RemovedUtc` is stamped and the row stays, so past bookings keep a name
against them and last season's class numbers do not change. "Mina bokningar" resolves names through
`GetAllForMemberAsync`, which includes removed children for exactly that reason.

Two things go with the child:

- **Their future bookings are cancelled and credited**, exactly as if the member had pressed
  *Avboka* on each. Left standing, the seat stayed reserved against the class's capacity and the
  child kept appearing on the coach's roster while the parent believed they were gone. Past
  bookings are untouched — cancelling those would rewrite attendance that already happened.
- **The account drops to solo** if that leaves one child. See above.

**Adding a child back restores the same row**, matched on name and birth date within the account.
It must: the welcome price lives on the participant, so a fresh row would arrive with
`FirstClassUsedUtc` null and hand the same child a second trial class — and their bookings would be
split across two rows nobody can pair up. The match is done in memory rather than in SQL, because
SQLite's default comparison is case-sensitive and `COLLATE NOCASE` folds only ASCII, which would
make *Åsa* and *åsa* two different children.

`NdstkStrandedBookingCleanup` releases places left held by children removed before that rule
existed. Guarded by a key/value marker like the participant backfill, so it runs once per database.

### A child's name and birth date are fixed once saved

They identify a person on a class roster, and a coach cannot trust a list a parent can rewrite after
the fact. `TryCompleteAsync` only touches a row whose `BirthDate` is still null — the rule is in the
`WHERE` clause, not in the absence of a form.

That null is the exception that has to stay: `NdstkParticipantBackfill` creates children from email
local parts with no birth date, and `BookAsync` refuses a child without one. Without a way to
correct those placeholders, the accounts holding them could never book.

### A class you are already on drops off the list

"Boka träning" only lists classes there is still someone to book. A class every child on the account
is already on is in *Mina bokningar* a few lines above; leaving it in the list too — stripped of its
buttons, carrying only a "Bokad:" label — reads as something you failed to do rather than something
you have done.

`BookableClass.EveryChildBooked` is deliberately not the same as `MemberHasBooking`. A family with
one of two children booked keeps the class, because there is a real action left on it: the other
child. An anonymous visitor never satisfies it — with no children, nothing is booked — so the list
stays a full shop window.

Two empty states, not one. No classes at all says so; being booked on all of them says *that*,
because telling a member "inga träningar är upplagda" when they are booked on every one would read
as though the club had cancelled the term.

## Cancellation closes before the class

A place given up an hour before the class cannot realistically be filled by anybody else, so a late
cancellation costs the club a coached slot and the member nothing. Cancelling therefore closes a set
number of hours before the start — **Avbokning stänger (timmar innan)** on the Settings node,
default 12.

The rule lives in `Cancellation` in `NDSTK.Domain`, in two shapes that have to agree:

| | |
| --- | --- |
| `IsOpen(classStart, now, hours)` | what the portal renders with |
| `EarliestCancellableStart(now, hours)` | the cutoff the `UPDATE`'s `WHERE` compares against |

SQL cannot add hours to "now" portably, so the service computes the cutoff once and passes it down.
A test pins the two to the same boundary — exactly on the deadline is **closed**, because closing
early is the direction that matches having a deadline at all.

**The button is disabled, not removed.** A control that vanishes leaves a member wondering whether
they missed it; one that is visibly closed, with the reason on it, tells them the rule for next
time. That is presentation only: the deadline is a precondition of the `UPDATE`, so a replayed form
cannot slip a late cancellation through. Verified by lifting a cancel form's `ufprt` from an older
page and POSTing it — refused, with the booking left `Confirmed`.

Being inside the deadline gets its **own** message naming the hours, unlike the other refusals which
deliberately share one. It is only ever reached for the member's own confirmed booking, so it
reveals nothing, and "kan inte avbokas" would read as a fault rather than a deadline.

Zero counts as "not set" and falls back to the default, like every other field on that node —
Umbraco's numeric editor cannot tell an emptied field from a deliberate 0, so there is no way to
express "no deadline" through it. For a club that does not want late cancellations, that is the
safer of the two readings.
