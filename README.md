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
`dotnet build -t:CoreCompile` to type-check without linking.

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

Register → confirm the emailed link → sign in → book a class → pay through the mocked Swish page.

Prices live on the **Settings** node under *Medlemskap*, in kronor:

| Field | Default |
| --- | --- |
| Årsavgift | 150 |
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
  Membership runs 365 days from the day the payment completes.
- The 100 kr welcome price is **once per account, for life**. A lapsed member renewing pays
  150 + 200, not 150 + 100.
- Cancelling gives no refund. It issues one **credit**, worth one place on any class with room.
- Spending a credit costs nothing, so a paid-up member skips the payment step entirely. A lapsed
  member spending a credit still pays the annual fee.

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

Credits are a **ledger, not a counter** — spending one is a conditional `UPDATE`, which cannot
double-spend, and the rows are an audit trail.

Reserving a place is a **single conditional `INSERT`**, so two members clicking at the same moment
cannot both take the last place. Verified with 60 concurrent attempts at a capacity-8 class. Note
that SQLite serialises writers, which helps this hold; on SQL Server the same statement would want a
lock hint.

Datetimes are stored as TEXT in NPoco's `yyyy-MM-dd HH:mm:ss.fffffff`. **Do not hand-format a date
into raw SQL** — round-trip `"o"` format sorts differently as text (`T` above space), which silently
breaks the reminder window.

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
