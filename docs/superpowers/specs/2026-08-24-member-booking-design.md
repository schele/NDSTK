# Member booking system — design

Date: 2026-08-24
Branch: `feature/member-booking`
Target: NDSTK, Umbraco 18.1.1 on .NET 10, SQLite

## Purpose

Turn the site's dead "Bli medlem" call to action into a working member area: visitors
register and confirm their email address, members book training classes and pay through a
mocked Swish flow, and the club administers classes and their capacity from the backoffice.

## Scope

In scope:

- Member registration with email confirmation, and member login and logout.
- A member portal listing bookable classes with live remaining capacity.
- Booking a class, priced per the rules below, paid through a mocked Swish provider.
- Cancelling a booking, which issues a booking credit instead of a refund.
- Booking with a credit when a place is free.
- A reminder banner in the portal and a reminder email, both 24 hours before a class.
- Class management in the backoffice, including a maximum number of participants.

Out of scope, and why:

- Password reset. Not requested; a clean follow-up once registration exists.
- Recurring class series. Classes are individual occurrences; a weekly generator is
  editor convenience, not a requirement.
- A real Swish integration. Mocked behind a provider interface so it can be added later.
- Refunds of any kind. The club keeps the money and issues a credit.
- English copy for the member area. The site defaults to Swedish; member-facing text is
  Swedish only.

## Verified platform facts

Every API below was verified by compiling against the Umbraco 18.1.1 assemblies rather
than read from documentation. Three findings shaped the design:

- `MigrationBase` **does not exist** in Umbraco 18. Migrations derive from
  `Umbraco.Cms.Infrastructure.Migrations.AsyncMigrationBase` and override
  `MigrateAsync()`.
- `IMemberSignInManager` is in `Umbraco.Cms.Web.Common.Security`, not
  `Umbraco.Cms.Core.Security`. `PasswordSignInAsync` takes a **username string**, not a
  `MemberIdentityUser`.
- `SmtpSettings` exposes `DeliveryMethod` and `PickupDirectoryLocation`, so writing mail
  to disk during development is configuration only — no custom `IEmailSender`.

Also confirmed present: `IMemberManager` with the inherited ASP.NET Identity members
`FindByEmailAsync`, `CreateAsync`, `GenerateEmailConfirmationTokenAsync` and
`ConfirmEmailAsync`; `IEmailSender.SendAsync(EmailMessage, string)`;
`IRecurringBackgroundJob` in `Umbraco.Cms.Infrastructure.BackgroundJobs`;
`IServerRoleAccessor` in `Umbraco.Cms.Core.Sync`; `IPublicAccessService`; and the
`SecuritySettings` members `MemberPassword`, `MemberRequireUniqueEmail` and
`MemberDefaultLockoutTimeInMinutes`.

## Architecture

### Pages

All pages are Umbraco content nodes created by the existing code-first installer, so
editors own their copy and their URLs.

| URL | Document type | Access | Purpose |
| --- | --- | --- | --- |
| `/bli-medlem` | `memberRegister` | Public | Registration form |
| `/logga-in` | `login` (existing) | Public | Login form, replacing the BankID placeholder copy |
| `/verifiera` | `memberVerify` | Public | Email confirmation landing page |
| `/medlem` | `memberPortal` | Members | Bookings, reminders, credits, bookable classes |
| `/medlem/betalning` | `swishPayment` | Members | Mocked Swish payment page |

Member-only access is enforced through `IPublicAccessService` against a **Medlemmar**
member group, so Umbraco's own pipeline does the gating rather than a check in a view.

The two existing dead `#members` links — the hero on Start and the `ctaWidgetBlock` on
Settings, both in `NdstkContentSeeder` — are repointed at `/bli-medlem`.

### Content model additions

`trainingClasses` (folder) contains `trainingClass` nodes with:

| Property | Type | Notes |
| --- | --- | --- |
| `title` | Textstring | Falls back to the node name |
| `description` | Textarea | |
| `start` | DatePicker with time | The class start, in Swedish local time |
| `durationMinutes` | Numeric | |
| `capacity` | Numeric | The maximum number of participants |
| `instructor` | Textstring | |
| `location` | Textstring | |

`trainingClass` gets no template. Classes are data, not pages.

**Time zones.** The backoffice date picker gives an editor typing "18:00" a value with no
offset, meaning 18:00 in Sweden. Treating that as UTC would fire every reminder one or two
hours early depending on the season. The class start is therefore read as
`Europe/Stockholm` local time and converted to UTC on the way into the database, using
`TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm")`, which resolves on both Windows
and Linux under .NET 10. The property is named `start`, not `startUtc`, so nothing suggests
the editor is typing UTC.

Prices are deliberately **not** per class. The existing `settings` document type gains a
"Medlemskap" group so all money lives in one place:

| Property | Type | Default |
| --- | --- | --- |
| `membershipFee` | Numeric | 150 |
| `firstClassPrice` | Numeric | 100 |
| `classPrice` | Numeric | 200 |
| `reminderHoursBefore` | Numeric | 24 |
| `paymentHoldMinutes` | Numeric | 15 |
| `memberPortalPage` | ContentPicker | Redirect target after login |
| `registerPage` | ContentPicker | Call-to-action target |

The `Member` member type gains `membershipPaidUntil` (date) and `firstClassDiscountUsed`
(true/false). Both are visible in the backoffice, so an administrator can comp a
membership or reset a discount by hand.

### Database

Three tables, created by one `AsyncMigrationBase` migration in a `MigrationPlan` run from
a composer, following the same install-on-boot shape as `NdstkContentModelInstaller`.

`ndstkBooking`

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int identity | Primary key |
| `MemberKey` | Guid | Indexed |
| `ClassKey` | Guid | Indexed |
| `ClassStartUtc` | datetime | Denormalised from the class node so the reminder query is one indexed range scan and does not touch the content cache |
| `Status` | nvarchar(20) | `Pending`, `Confirmed`, `Cancelled`, `Expired` |
| `PaymentId` | int null | |
| `HoldExpiresUtc` | datetime null | Set while `Pending` |
| `CreatedUtc` | datetime | |
| `ConfirmedUtc` | datetime null | |
| `CancelledUtc` | datetime null | |
| `ReminderSentUtc` | datetime null | |

`ndstkPayment`

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int identity | Primary key |
| `Reference` | Guid | Unique. The value that appears in the payment URL |
| `MemberKey` | Guid | |
| `BookingId` | int null | |
| `AmountOre` | int | Total charged |
| `MembershipFeeOre` | int | The membership part of the total |
| `ClassFeeOre` | int | The class part of the total |
| `Status` | nvarchar(20) | `Pending`, `Paid`, `Failed`, `Cancelled` |
| `Provider` | nvarchar(50) | `SwishMock` |
| `CreatedUtc` | datetime | |
| `CompletedUtc` | datetime null | |

`ndstkBookingCredit`

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int identity | Primary key |
| `MemberKey` | Guid | Indexed |
| `SourceBookingId` | int | The cancelled booking that produced it |
| `SpentOnBookingId` | int null | Null means unspent |
| `IssuedUtc` | datetime | |
| `SpentUtc` | datetime null | |

Two decisions worth stating plainly:

**Money is stored as integer öre.** SQLite has no decimal type and maps `decimal` to
`REAL`, so a `decimal` column puts floating-point rounding into payment records. Integers
remove the problem rather than manage it.

The link between a booking and the credit spent on it is held **only** on the credit row,
as `SpentOnBookingId`. An additional `UsedCreditId` on the booking would be a second copy
of the same fact and the two could drift apart; one direction cannot disagree with itself.

**Credits are a ledger, not a counter.** Spending a credit is
`UPDATE ndstkBookingCredit SET SpentOnBookingId = @id WHERE Id = @credit AND SpentOnBookingId IS NULL`,
which cannot double-spend regardless of concurrency, and the table doubles as an audit
trail. A single "credits remaining" integer on the member would need its own locking and
would lose the history.

### Payment holds

A `Pending` booking reserves a place while the member is on the Swish page. If they close
the tab that place would be reserved forever, so:

- Creating a booking sets `HoldExpiresUtc = now + paymentHoldMinutes`.
- Remaining capacity counts `Confirmed` bookings plus `Pending` bookings whose hold has
  not expired.
- The background job sweeps expired holds to `Expired` and releases any credit spent on
  them.

Without this, classes silently fill with abandoned bookings.

### Overbooking

Counting places and inserting a booking happen inside a single `IScope` from
`Umbraco.Cms.Infrastructure.Scoping.IScopeProvider`. SQLite serialises writers, so the
count cannot go stale between the check and the insert within that scope. A partial unique
index on `(MemberKey, ClassKey) WHERE Status IN ('Pending','Confirmed')`, added with raw
SQL in the migration, stops the same member holding two live bookings for one class while
still allowing a rebooking after a cancellation.

### When an editor changes a class

`ClassStartUtc` is denormalised onto the booking, which means an editor moving a class in
the backoffice would leave every existing booking pointing at the old time and send its
reminders at the wrong hour. A `ContentPublishedNotification` handler therefore resyncs
`ClassStartUtc` on all live bookings for that class whenever a `trainingClass` is
published. Bookings whose reminder had already been sent for the old time have
`ReminderSentUtc` cleared if the class moved later, so the member is told about the change.

Two related editor actions:

- **Unpublishing or deleting a class** with live bookings cancels those bookings and issues
  a credit for each, so nobody silently loses a paid place. Handled from
  `ContentUnpublishedNotification` and `ContentDeletedNotification`.
- **Reducing capacity below the number of places already taken** is allowed. Existing
  bookings stand — the club is not going to turn away someone who paid — and no further
  bookings are accepted until the count falls below the new capacity.

## Pricing

A pure function, with no dependency on Umbraco or the database, so it is exhaustively
testable:

```
Quote(member, prices, useCredit):
    membershipDueOre = member.MembershipPaidUntil is null
                       or member.MembershipPaidUntil < today
                       ? prices.MembershipFeeOre
                       : 0

    classFeeOre = useCredit                        ? 0
                : member.FirstClassDiscountUsed    ? prices.ClassPriceOre
                                                   : prices.FirstClassPriceOre

    totalOre = membershipDueOre + classFeeOre
```

Consequences, all intended:

- A lapsed member booking their first class pays 150 + 100 = 250 kr in one payment.
- Later classes cost 200 kr while the membership is valid.
- A member with a valid membership spending a credit owes 0, so the Swish step is skipped
  entirely and the booking is confirmed immediately.
- A member with a lapsed membership spending a credit still pays the 150 kr fee.
- `FirstClassDiscountUsed` is set only when a payment that actually included the
  discounted class fee completes, so spending a credit never consumes the discount.

Membership runs 365 days from the day the payment completes: `MembershipPaidUntil =
today + 365 days`. The discounted first class is once per account, for life.

## Flows

### Registration and verification

1. `RegisterSurfaceController.Register` validates the form.
2. It creates the member through `IMemberManager.CreateAsync` with `IsApproved = false`.
3. It generates a token with `GenerateEmailConfirmationTokenAsync`.
4. It sends a mail from `info@ndstk.se` containing
   `/verifiera?member={key}&token={token}`.
5. The verification page calls `ConfirmEmailAsync`; on success it sets `IsApproved = true`
   and invites the member to log in.

`IsApproved = false` is the substantive gate. Umbraco's own member sign-in already refuses
unapproved members, so an unverified account cannot log in even if a check in our own
controller were bypassed. The controller checks `EmailConfirmed` as well; the two together
mean no single mistake opens the door.

### Login

`LoginSurfaceController` calls `IMemberSignInManager.PasswordSignInAsync(email, password,
isPersistent, lockoutOnFailure: true)` and redirects to the portal page configured on
Settings. Logout posts to a companion action and calls `SignOutAsync`.

### Booking and payment

1. The member posts a class key, optionally requesting a credit.
2. The booking service takes a scope, checks capacity, and writes a `Pending` booking with
   a hold.
3. It quotes the price. If the total is zero the booking is confirmed immediately and the
   member returns to the portal.
4. Otherwise it writes a `Pending` payment and redirects to
   `/medlem/betalning?ref={reference}`.
5. The payment page renders the existing `swish-logo.png`, a fake QR code, the reference
   and a breakdown of the amount, with **Simulera betalning** and **Simulera avbrott**
   buttons.
6. Simulating payment marks the payment `Paid`, the booking `Confirmed`, clears the hold,
   extends `membershipPaidUntil` if the membership fee was included, and sets
   `firstClassDiscountUsed` if the discounted class fee was included.
7. Simulating a cancellation marks the payment `Cancelled` and the booking `Expired`,
   returning any credit.

The mock lives behind an `IPaymentProvider` interface implemented by
`SwishMockPaymentProvider`. A real Swish integration is then a second implementation and a
DI registration, with nothing else changing.

The payment page and both simulate actions verify that the payment reference belongs to
the signed-in member. Without that check, guessing a reference would let anyone confirm
anyone else's payment.

### Cancellation, credit and rebooking

Cancelling posts to a surface controller that verifies ownership, sets the booking
`Cancelled`, frees the place, and inserts one unspent credit. No money is returned.

The portal then shows the number of unspent credits, and every class with a free place
offers **Boka med tillgodoklass** alongside the paid button. Spending a credit follows the
booking flow above with `useCredit = true`.

### Reminders

`ClassReminderJob` implements `IRecurringBackgroundJob` with a 15 minute period, guarded
by `IServerRoleAccessor` so it runs only on the scheduling publisher — otherwise a
multi-server deployment would send every reminder more than once. Each run:

1. Selects `Confirmed` bookings whose `ClassStartUtc` falls inside the next
   `reminderHoursBefore` hours and whose `ReminderSentUtc` is null.
2. Sends a reminder mail per booking and stamps `ReminderSentUtc`.
3. Sweeps `Pending` bookings whose hold has expired.

Stamping per booking makes the job idempotent: a crash halfway through resends nothing
already sent.

The reminder banner is portal-only, per the agreed scope: a highlighted card at the top of
"Mina bokningar" listing classes starting inside the same window. It is a pure read with
no state, so it cannot disagree with the email.

## Mail

`Umbraco:CMS:Global:Smtp` in `appsettings.json`:

| Setting | Value |
| --- | --- |
| `Host` | `send.one.com` |
| `Port` | `465` |
| `SecureSocketOptions` | `SslOnConnect` |
| `From` | `info@ndstk.se` |
| `Username` | `info@ndstk.se` |

The password belongs in `appsettings.Secrets.json`, which is already gitignored and
already loaded by `Program.cs`. It is never committed and never printed.

`appsettings.Development.json` overrides `DeliveryMethod` to
`SpecifiedPickupDirectory` with a local `umbraco/Logs/Mail` folder, so every message lands
on disk as a `.eml` file. The whole registration and reminder flow is therefore testable
locally with no live SMTP account.

Three templates, as Razor partials rendered to string so the copy sits with the rest of
the views: verification, booking confirmation, and class reminder.

## Security

The registration form, and every other member-facing POST:

- Antiforgery token, plus `[ValidateUmbracoFormRouteString]` on the surface controller.
- Server-side validation of everything: email format, password against Umbraco's
  `MemberPassword` policy, confirmation match. Client-side validation is convenience only.
- **Resistant to account enumeration.** Registering an address that already exists returns
  the identical "kolla din inkorg" response as a new address, so the form cannot be used
  to discover who is a member.
- A honeypot field and a minimum form-fill time, against unsophisticated bots.
- Per-IP rate limiting on register, login and verify, using the ASP.NET Core rate limiter.
- Member lockout after repeated failures, through `MemberDefaultLockoutTimeInMinutes` and
  `lockoutOnFailure: true`.
- `MemberRequireUniqueEmail` enabled, since the email is the username.
- Single-use, expiring confirmation tokens. Tokens and passwords are never logged.
- Ownership checks on every action that touches a booking or a payment.
- Cookies are already HTTPS-only through the existing `UseHttps` setting.

## Testing

A new `NDSTK.Tests` xUnit project, added to `NDSTK.slnx`, covering the logic where a bug
is both likely and silent:

- Pricing: every combination of membership valid or lapsed, discount used or not, credit
  or not, including the zero-total case.
- Capacity: booking the last place, booking a full class, expired holds freeing places.
- Credits: issue on cancel, spend once, no double-spend, return on expiry.
- Reminders: selection window boundaries, and no repeat once stamped.
- Verification: unapproved members cannot log in; a used or wrong token is rejected.
- Time zones: a class at 18:00 Swedish local time converts to the correct UTC instant in
  both summer and winter, so a reminder for a July class is not an hour out.

These are pure functions and services behind repository interfaces, so the suite needs
neither a database nor an Umbraco boot and runs in seconds.

## Implementation phases

Each phase ends with the solution building and something demonstrable.

| Phase | Work | Estimate |
| --- | --- | --- |
| 1 | Migration and three tables, repositories, member type properties, Settings fields, test project | 45–60 min |
| 2 | Registration, verification, login and logout, mail configuration, form security | 60–90 min |
| 3 | `trainingClass` document type and backoffice, portal class list with live capacity | 40–50 min |
| 4 | Pricing engine, booking, Swish mock page, confirm and fail | 60–90 min |
| 5 | Cancellation, credit ledger, rebooking with a credit | 30–45 min |
| 6 | Reminder job, reminder mail, portal banner, hold sweeper, editor-change handlers | 50–70 min |
| 7 | Repoint the calls to action, seed content, styling, notes | 30–45 min |

## Assumptions

Stated so they can be corrected rather than discovered:

- Cancellation is allowed at any time before the class starts. There is no deadline, and
  no penalty beyond losing the money.
- Classes are individual occurrences. There is no recurrence generator.
- The member area is Swedish only.
- A member may hold at most one live booking per class.
- Reminders go only to `Confirmed` bookings. A member sitting on an unpaid hold 24 hours
  before a class is not reminded, because they have not booked.
