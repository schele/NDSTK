# Swish payment — design

Date: 2026-09-02
Branch: `feature/swish-payment`
Target: NDSTK, Umbraco 18.1.1 on .NET 10. SQLite locally, SQL Server in production.
Follows: `2026-08-25-member-administration-design.md`

## Purpose

Members pay for bookings through a page that only pretends to be Swish. Two buttons stand in
for the answer the Swish app would give. Replacing that with the real thing was always the
plan, and the README says it is "a second `IPaymentProvider` and one line in
`BookingComposer`". It is not. The interface carries a name and a boolean, settlement is not
safe to run twice, nothing can receive or fetch a result from Swish, and the payment row has
nowhere to keep Swish's identifiers. This design makes the site Swish ready: it takes real
payments through Swish Commerce when a certificate is configured, and keeps working exactly
as today when one is not.

## Scope

In scope:

- Swish Commerce **m-commerce** payment requests over the v2 API with the merchant
  certificate: create, retrieve, cancel.
- **App switch** on phones and a **QR code** on desktops, both from the same payment request.
- A payment page that starts the payment on the member's action, then polls until Swish
  answers.
- An anonymous **callback endpoint** for Swish, verified by the per-payment callback
  identifier, that never trusts the callback body.
- **Reconciliation**: a lost callback cannot strand a paid booking.
- **Idempotent settlement**: the callback, the poll and the job may all race, and exactly one
  of them settles.
- A rule for money that arrives after the reservation lapsed.
- Schema for Swish's identifiers on `ndstkPayment`, and the Swish reference in the backoffice.
- The mock stays, chosen by configuration, so local development needs no certificate.
- A go-live checklist covering the bank agreement, the certificate and the IIS binding.

Out of scope, and why:

- **Refunds.** The club's model has none; a cancelled booking becomes a credit. The one case
  where money arrives for a place that no longer exists is handled with a credit too, below.
- **The e-commerce flow** where the member types a phone number. Decided against for the first
  version; the data model does not preclude adding it.
- **Recurring payments, payouts, age limits.** Not needed by a tennis club's class booking.
- **IP allowlisting of the callback.** Recommended by Swish, but the callback is not trusted
  anyway. Listed in the checklist as optional hardening on the server.
- **Storing the payer's phone number** from the callback. It is personal data the club has no
  use for; the guardian's number is already on the account.

## Decisions taken during brainstorming

| Question | Decision |
| --- | --- |
| Which Swish flow first? | App switch on phones, QR code on desktops (m-commerce). |
| Where is the truth about a payment? | Swish, fetched over mTLS. The callback is a nudge. |
| What if the callback never arrives? | Polling from the page, then the job. Both settle. |
| Reservation length | 7 minutes, restarted when the member starts the Swish payment. |
| Paid after the reservation lapsed | Re-confirm if a place is free, otherwise issue a credit. |
| Merchant agreement | Not signed yet. Verified against the simulator only until it is. |
| Who chooses mock or Swish? | Configuration. No certificate means the mock, and the page says so. |

## Verified Swish facts

From the Merchant Integration Guide 2.6 and the Merchant Swish Simulator guide 1.6, both
published by Swish, cross-checked against the current developer site through search
snippets because the site itself renders client-side. Version 1 of the create call was
decommissioned in early 2026; retrieve and cancel remain on their v1 paths.

- Create: `PUT {api}/api/v2/paymentrequests/{instructionUUID}`, body JSON, response 201 with
  a `Location` header and, when no `payerAlias` is sent, a `PaymentRequestToken` header. The
  token is what opens the app and what the QR encodes.
- Retrieve: `GET {api}/api/v1/paymentrequests/{id}` returns the payment request object with
  `status` CREATED, PAID, DECLINED, ERROR or CANCELLED, `paymentReference` when PAID,
  `errorCode` and `errorMessage` when ERROR.
- Cancel: `PATCH` the same URL with content type `application/json-patch+json` and body
  `[{"op":"replace","path":"/status","value":"cancelled"}]`. Only while CREATED; otherwise
  422 with error code RP07.
- `instructionUUID`: 32 uppercase hexadecimal digits, no hyphens.
- `payeePaymentReference`: 1 to 35 characters from `a-z A-Z 0-9 -`.
- `payeeAlias`: the club's Swish number. `amount`: a string with up to two decimals,
  `"150.00"`. `currency`: `SEK`.
- `message`: at most 50 characters; letters a-ö and A-Ö, digits, space and `:;.,?!()"`.
- `callbackUrl`: HTTPS. `callbackIdentifier`: 32 to 36 characters from `0-9 a-z A-Z -`,
  echoed back verbatim as a `callbackIdentifier` header on the callback. Swish recommends a
  fresh value per request as the way to authenticate callbacks.
- Callback: HTTPS POST of the payment request object. Retried up to ten times at 5, 10, 20,
  40 and then 60 second intervals until the endpoint answers 200. The app times a request
  out after 3 minutes, the backend after 5.5 minutes for m-commerce, and then sends ERROR
  with code TM01.
- The callback client validates the merchant's certificate against public CAs and **does not
  send SNI**. Production callbacks come from 213.132.115.94.
- Hosts: `https://cpc.getswish.net/swish-cpcapi/` in production,
  `https://mss.cpc.getswish.net/swish-cpcapi/` for the simulator. The server certificate
  chains to DigiCert Global Root CA.
- Merchant certificate: a 4096-bit RSA key pair, CSR pasted into portal.swish.nu by the
  club's certificate contact, valid two years.
- Simulator: certificate `Swish_Merchant_TestCertificate_1234679304.p12`, password `swish`.
  It ignores a mismatch between `payeeAlias` and the certificate, calls back about four
  seconds after create, and simulates outcomes when `message` is an error code: RF07, FF10,
  TM01, DS24 and BANKIDCL arrive as ERROR in the callback; BE18, RP03, AM06 and the other
  validation codes come back synchronously as 422. Its cache expires after 24 hours.
- QR: `POST https://mpc.getswish.net/qrg-swish/api/v1/commerce` with
  `{"token":"…","format":"svg","size":300}` returns the image. The documentation shows no
  certificate on this call; confirmed at implementation.
- App switch: `swish://paymentrequest?token=<token>&callbackurl=<url-encoded return URL>`.

## Verified hosting facts

ndstk.se resolves to a dedicated Sharktech address in Amsterdam running IIS 10 with a Let's
Encrypt certificate for `ndstk.se` and `www.ndstk.se`. A TLS handshake **without SNI is
dropped with no certificate**: the https binding requires Server Name Indication and there is
no fallback binding. Swish's callback client does not send SNI, so **callbacks fail against
the site as bound today**. The fix is in IIS, not code: untick *Require Server Name
Indication* on the binding, or add a binding on the IP address with the same certificate.
The design does not depend on the fix, but the checklist requires it before go-live.

## Architecture

### The provider interface

`IPaymentProvider` becomes the boundary it was meant to be:

```csharp
public interface IPaymentProvider
{
    string Name { get; }                       // "Swish" or "SwishMock", stored on the row

    // Creates the payment request at the provider. Returns what the page needs to hand the
    // member over: the token for the app switch and the QR code.
    Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context);

    // Asks the provider what happened. Never throws for a terminal state; throws only when
    // the provider cannot be reached, so callers can tell "declined" from "unknown".
    Task<PaymentOutcome> RetrieveAsync(string providerReference);

    // Withdraws a request the member has not answered. Idempotent: a request that is already
    // terminal is reported as such rather than as a failure.
    Task<PaymentOutcome> CancelAsync(string providerReference);
}

public sealed record PaymentStartContext(string CallbackUrl, string Message);
public sealed record PaymentStart(string ProviderReference, string? Token, string CallbackIdentifier);
public sealed record PaymentOutcome(
    ProviderStatus Status, string? BankReference, string? ErrorCode, DateTime? PaidUtc);
public enum ProviderStatus { Created, Paid, Declined, Error, Cancelled }
```

Two implementations:

- `SwishPaymentProvider` in `Booking/Payments/Swish/`. A named `HttpClient` with the merchant
  certificate on its handler. Builds the request from the pure rules below, reads the
  `PaymentRequestToken` header, maps the response. 422 bodies are parsed for their error
  codes and surfaced as a typed exception so the page can say why.
- `SwishMockPaymentProvider` keeps its two buttons. `StartAsync` returns a made-up token and
  reference. `RetrieveAsync` reads the row's own status, so the shared settlement path is the
  same code for both providers.

`BookingComposer` registers one of them from `SwishOptions`: enabled with a certificate
means Swish, anything else means the mock. The boot log states which, at warning level for
the mock, so a production log that took no money says so on its first line.

### Pure rules in `NDSTK.Domain`

Everything Swish constrains that can be got wrong silently, as pure functions with tests:

```csharp
public static class SwishRequest
{
    public static string InstructionId(Guid reference);            // "N" format, upper case
    public static string PaymentReference(Guid reference);         // same value, fits 1–35 alnum
    public static string Amount(int ore);                          // "150.00", invariant culture
    public static string Message(string? classTitle, DateTime? classStartSwedish);
                                                                   // sanitised, at most 50
    public static string CallbackIdentifier();                     // fresh Guid, "N" format
    public static string AppLink(string token, string returnUrl);  // swish://…, encoded once
}

public static class SwishOutcome
{
    // What a Swish status and error code mean to this site: the PaymentStatus to store,
    // whether the state is final, and the Swedish sentence the member sees.
    public static PaymentResolution Resolve(string status, string? errorCode);
}

public sealed record PaymentResolution(bool IsTerminal, string PaymentStatus, string MemberMessage);
```

`Message` strips anything outside the allowed set, collapses whitespace and truncates to
fifty characters. The text is `Träning {d MMMM HH:mm}` for a class and `Familjekonto` for an
upgrade, so it reads correctly in the member's Swish history. It never contains a hyphen,
which the 2020 specification omits and the simulator guide allows.

`Resolve` maps DECLINED to `Cancelled` ("Du avböjde betalningen i Swish."), CANCELLED to
`Cancelled`, PAID to `Paid`, and ERROR to `Failed` with a message per code: RF07 the bank
declined, BANKIDCL the BankID signing was cancelled, FF10 a bank error, TM01 the request
timed out before it was answered, DS24 the outcome is unknown and the member should check
their Swish app before trying again. Unknown codes get a generic sentence and the code is
logged. CREATED is not terminal.

### Schema: `ndstkPayment` gains Swish's identifiers

One new step in `BookingMigrationPlan`, `swish-1`, following the add-column-if-missing shape
of `AddParticipantTable`:

```
ProviderReference   nvarchar(36)  null   -- instructionUUID; what the callback names
ProviderToken       nvarchar(64)  null   -- PaymentRequestToken; the page needs it on reload
CallbackIdentifier  nvarchar(36)  null   -- compared against the callback header
BankReference       nvarchar(64)  null   -- Swish paymentReference, set when PAID
ErrorCode           nvarchar(20)  null   -- Swish errorCode, set when ERROR
StartedUtc          datetime      null   -- when the request was created at Swish
LastCheckedUtc      datetime      null   -- last RetrieveAsync, for reconciliation pacing
```

plus a unique filtered index on `ProviderReference WHERE ProviderReference IS NOT NULL`.
The filter matters on SQL Server, which treats nulls as equal in a unique index and would
refuse the second row that has not started a payment. Both engines accept the statement.

`BookingSchemaSql` gains `AddNullableStringColumn`, `AddNullableDateTimeColumn` and
`CreateFilteredUniqueIndex`, each emitting one line per dialect, tested like the existing
statements. SQLite's `TEXT NULL` for both string and datetime matches how NPoco already
stores every date in these tables.

`PaymentStatus` is unchanged. `Failed` finally gets used.

### Settlement becomes one conditional write

`IBookingRepository.CompletePaymentAsync` is replaced by

```csharp
Task<bool> TryCompletePaymentAsync(
    int paymentId, string status, DateTime nowUtc, string? bankReference, string? errorCode);
// UPDATE ndstkPayment SET Status=@status, CompletedUtc=@now, BankReference=…, ErrorCode=…
// WHERE Id=@id AND Status='Pending'
```

`BookingService.SettlePaymentAsync` runs the side effects only when that update changed a
row. The callback, the page's poll and the job can all arrive with the same PAID within the
same second; one wins, the others log "already settled" and stop. This is the property the
mock's read-then-write never had.

`AbandonPaymentAsync` uses the same conditional update, so a DECLINED arriving after a TM01
changes nothing.

### Money that arrives late

Swish can settle up to 5.5 minutes after the request, and retry the callback for another
seven. The reservation is restarted at 7 minutes when the payment starts, so in the normal
course the hold outlives the request. But nothing guarantees ordering across a lost
callback, so `SettlePaymentAsync` reads the booking after winning the payment update:

- **Pending** — confirm it, as today.
- **Expired** by the sweep — `TryReconfirmBookingAsync(bookingId, capacity, nowUtc)`: a
  conditional `UPDATE … SET Status='Confirmed' WHERE Id=@id AND Status='Expired' AND (live
  count for the class) < @capacity`, the same counting subquery `TryReservePlaceAsync` uses.
  If it succeeds the member simply has their place. If the class filled in the meantime,
  `IssueCreditAsync(memberKey, bookingId, nowUtc)` writes one credit row, exactly as a
  cancellation would, and the page says so: "Platsen hann ta slut medan betalningen
  genomfördes. Du har fått en tillgodoträning att boka en annan träning med."

The membership extension, the family flag and the welcome-price stamp run in every paid
case, unchanged: the fee was paid, and the credit is worth a class.

### The payment page

`SwishPaymentController` renders one of four states from the row:

1. **Not started.** The breakdown as today, and one form: *Betala med Swish*, plus *Avbryt*.
   No request exists at Swish yet, so the member is in control of when one is made, which
   Swish's guidelines ask for.
2. **Started.** After `Start` the page shows the countdown from the restarted hold and, by
   device: on a phone a prominent *Öppna Swish* link built by `SwishRequest.AppLink`, whose
   return URL is this page; on a desktop the QR code and "Skanna med Swish-appen". A small
   link swaps between the two. Below both: "Väntar på att du godkänner i Swish …" and
   *Avbryt*. Device choice is client-side; without JavaScript both blocks render.
3. **Paid.** "Klart! Din träning är bokad." and the portal link, or the credit sentence above.
4. **Failed or cancelled.** The sentence from `SwishOutcome.Resolve`, "Platsen är inte
   bokad", and a link back to the portal to try again.

`Demoläge` and the two simulate buttons render only when the mock is the active provider.

### Surface actions

`SwishPaymentSurfaceController`, all owner-checked as today, all under a new rate limit
policy `BookingRateLimits.PaymentStatus` generous enough for a 3-second poll from two tabs:

- `POST Start(reference)` — only when `ProviderReference IS NULL` and the payment is Pending.
  Calls `StartAsync`, stores reference, token, callback identifier and `StartedUtc`, and
  restarts the booking's hold to now plus the configured minutes in one conditional update.
  A provider exception leaves everything untouched and shows "Swish går inte att nå just nu.
  Försök igen om en stund." with the hold still ticking. Redirects back to the page.
- `GET Status(reference)` — JSON `{ status, terminal }`. When the payment is Pending and has
  a provider reference and `LastCheckedUtc` is older than five seconds, it reconciles first
  (below). The page's script polls this every three seconds and reloads on `terminal`.
- `GET Qr(reference)` — the QR image for the row's token, fetched from Swish's QR service
  through a second named client without the certificate, cached in memory by reference for
  ten minutes, which outlives any request Swish will still honour. `image/svg+xml`.
- `POST Cancel(reference)` — `CancelAsync` when a request exists, then abandon with
  `Cancelled` and release the hold. A request that Swish reports as already terminal is
  reconciled instead of cancelled, so a member pressing *Avbryt* a second after paying does
  not lose a paid place.
- `POST SimulatePaid`, `POST SimulateCancelled` — kept, and answer 404 unless the mock is the
  active provider. That gate is the provider's name, not the environment.

### One reconciliation routine, three triggers

```csharp
// BookingService
Task<PaymentRecord> ReconcileAsync(PaymentRecord payment, DateTime nowUtc)
```

Stamps `LastCheckedUtc`, calls `RetrieveAsync`, and if the outcome is terminal settles or
abandons through the conditional writes above. It is called from:

1. **The page's poll**, as described. This is the path that works everywhere, including
   locally against the simulator, and including production before the IIS binding is fixed.
2. **The callback.** `SwishCallbackController`, an anonymous `[ApiController]` at
   `POST /api/swish/callback`. Attribute-routed controllers are already mapped on this site,
   which `MemberAdminController` proves, so no routing change is needed. It
   reads the JSON body only for `id`, loads the row by `ProviderReference`, and compares the
   `callbackIdentifier` header with the stored value in constant time. On a match it calls
   `ReconcileAsync`. It answers 200 in every case that is not a malformed body, including
   an unknown id or a wrong identifier, because a non-200 only buys ten retries of the same
   request; the mismatch is logged at warning level. Its own rate limit policy,
   `BookingRateLimits.Callback`, is sized for Swish's retry schedule, not for people.
3. **The job.** `ClassReminderJob` gains a step **before** the hold sweep: reconcile every
   Pending payment that has a provider reference and was started more than a minute ago.
   Before the sweep, so a payment whose callback was lost is settled before its booking is
   expired. The sweep then also marks the payment of every hold it releases as `Cancelled`
   when it has no provider reference, so abandoned payments stop lingering as Pending in the
   backoffice.

### Configuration

```json
"NDSTK": {
  "Swish": {
    "Enabled": false,
    "PayeeAlias": "",
    "ApiBaseUrl": "https://cpc.getswish.net/swish-cpcapi/",
    "QrApiBaseUrl": "https://mpc.getswish.net/qrg-swish/",
    "CertificatePath": "",
    "CertificatePassword": "",
    "CertificateThumbprint": "",
    "SimulateErrorCode": ""
  }
}
```

`SimulateErrorCode` is read only in the Development environment; see *Local development*.

`appsettings.json` ships `Enabled: false` and the production API host.
`appsettings.Development.json` points `ApiBaseUrl` at the simulator. The certificate path,
password and payee alias live in `appsettings.Secrets.json` locally and in environment
variables or the untracked secrets file on the server, like the mail password does today.

The certificate is loaded once, at registration, either from the PKCS#12 file with
`MachineKeySet` or from `LocalMachine\My` by thumbprint. Not with `EphemeralKeySet`: SChannel
cannot present an ephemeral key as a TLS client certificate, and both development and
production are Windows. The file, when used, sits outside the web root.

The callback URL is `{WebRouting:UmbracoApplicationUrl}api/swish/callback`, so it is right in
every environment without a new setting. Locally it names a host the simulator cannot reach,
which is fine: the poll settles the payment.

`MembershipSettings.Defaults.PaymentHoldMinutes` becomes 7, and the README's table and the
Settings field description say why: it must outlive Swish's 5.5-minute backend timeout.

### Backoffice

`AdminPaymentRow` gains `BankReference` and `ErrorCode`. The Medlemmar detail table shows a
*Swish-referens* column, which is what the club will match against its bank statement, and
the status cell shows the error code for a failed payment. One language key per language for
the column header, no other change.

## Flows

### Booking and paying, on a phone

1. The member presses *Boka* in the portal. `BookAsync` reserves the place and writes a
   Pending payment, as today, and redirects to the payment page.
2. The page shows the breakdown. The member presses *Betala med Swish*.
3. `Start` builds the request: instruction id and reference from the payment's Guid, the
   amount, the sanitised message, the callback URL, a fresh callback identifier, and the
   club's payee alias. `PUT` to Swish. The token, reference and identifier are stored, the
   hold restarts at 7 minutes, and the page reloads in the started state.
4. The member taps *Öppna Swish*. The Swish app opens with the request preloaded; they
   approve with BankID; the app returns them to the payment page.
5. Meanwhile the page has been polling `Status` every three seconds. Within a few seconds
   of the approval, a poll reconciles, finds PAID, wins the conditional update, confirms the
   booking, extends the membership if the fee was included, stamps the welcome price, and
   the page reloads to "Klart!".
6. Swish's callback arrives at about the same time, verifies, reconciles, and finds the
   payment already settled. It answers 200 and logs nothing louder than debug.

### Paying on a desktop

Steps 1 to 3 as above. The page shows the QR code. The member opens Swish on their phone,
scans, approves. Steps 5 and 6 follow.

### The member gives up

*Avbryt* cancels the request at Swish, marks the payment Cancelled, releases the place and
returns to the portal with "Betalningen avbröts, så platsen är inte bokad." Closing the tab
instead leaves the request to time out: Swish sends TM01 after 5.5 minutes, the callback or
the job's reconciliation records `Failed`, and the hold is released then rather than at the
7-minute sweep.

### The callback never arrives

Nothing changes for a member who stayed on the page. For one who closed it after paying, the
job reconciles within fifteen minutes. If the sweep had already expired the booking, the
place is re-confirmed while there is room, otherwise the member receives a credit.

### Local development

The mock is active unless `Enabled` is true and a certificate is configured, so the flow the
README describes is unchanged. To exercise the real provider, point `ApiBaseUrl` at the
simulator with its test certificate: `Start` returns a token, the QR renders, and the poll
reports PAID about four seconds later without any callback reaching the machine. Errors are
simulated through a development-only setting, `NDSTK:Swish:SimulateErrorCode`, which
replaces the message when set and is ignored outside the Development environment; the
message is otherwise built from the class, so no class title can trigger one by accident.

## Security

- Every member-facing action keeps the ownership check and antiforgery it has today.
- The callback is authenticated by the per-payment identifier and is never a source of
  truth; the row is only ever changed by what `RetrieveAsync` returns over mTLS.
- The callback identifier and the payment token are never logged. The instruction id is,
  because it is what support will match against Swish's own logs.
- The payer's phone number in Swish's payload is not stored.
- The certificate password is a secret in the same sense as the mail password, and the file
  is outside the web root. The private key is 4096-bit RSA as Swish requires.
- The status poll and the callback each have their own rate limit policy so neither can
  exhaust the member-actions budget, and the callback policy is per IP so a flood cannot
  starve Swish's genuine retries from its one address.
- A PKCS#12 that fails to load at boot disables Swish and falls back to the mock **with a
  warning**, never silently: a site that stops taking money must say so.

## Testing

Unit tests in `NDSTK.Tests`, over `NDSTK.Domain` only, as today:

- `SwishRequest`: instruction id is 32 upper-case hex digits; amount formats 15000 as
  `"150.00"` and 5 as `"0.05"` regardless of thread culture; message keeps å ä ö, drops
  everything outside the set, collapses whitespace, truncates at 50; app link encodes the
  return URL exactly once and keeps the token verbatim.
- `SwishOutcome.Resolve`: every documented code maps to a status, a terminal flag and a
  non-empty Swedish sentence; CREATED is not terminal; an unknown code is `Failed`.
- `BookingSchemaSql`: the new column and filtered index statements, per dialect, as text.

Against the simulator, from the development machine, recorded in the plan as verification
steps rather than automated: create returns a token; retrieve turns PAID; a simulated RF07
becomes `Failed` with the right sentence; cancel returns CANCELLED and the hold is released;
two overlapping reconciliations of one payment produce exactly one confirmed booking.

On the live site, once the certificate and the IIS binding exist: one real payment of the
smallest class price, the callback logged as received and verified, and the row showing a
bank reference.

## Go-live checklist

Outside the code, in order:

1. The club signs **Swish Handel** with its bank and names a certificate contact.
2. The contact generates a 4096-bit RSA key and CSR on the server, obtains the certificate
   at portal.swish.nu, and exports key and chain as a password-protected PKCS#12 outside the
   web root.
3. The IIS https binding for ndstk.se stops requiring SNI, or a fallback binding on the IP
   carries the same certificate. Verified with a handshake that sends no server name.
4. `Enabled`, `PayeeAlias`, `CertificatePath` and `CertificatePassword` are set on the
   server. The boot log states "Payment provider: Swish".
5. Optionally, the callback path is restricted to 213.132.115.94 in IIS.
6. One real payment, as above.

Until step 4 the site keeps taking mock payments and the page keeps saying Demoläge.

## Phases

1. **Foundations.** Domain rules and tests, the schema step, the widened provider interface
   with the mock implementing it, conditional settlement, the late-payment rule, the 7-minute
   default. Member-visible behaviour unchanged.
2. **Swish provider and page.** The client, options, certificate loading, `Start`, `Status`,
   `Qr`, `Cancel`, the four-state page and its script. Verified against the simulator.
3. **Callback and reconciliation.** The callback endpoint, the job step, the sweep's payment
   cleanup, the backoffice column.
4. **Documentation.** README sections replacing "Swish is mocked", the checklist, and the
   settings description.
