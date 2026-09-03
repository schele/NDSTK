# Swish Payment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take real payments through Swish Commerce behind the existing `IPaymentProvider`, with the mock still available when no certificate is configured.

**Architecture:** The payment row gains Swish's identifiers. One `BookingService.ReconcileAsync` fetches the truth from Swish and settles through conditional updates; it is triggered by the page's poll, by Swish's callback, and by the reminder job. `SwishPaymentProvider` speaks the v2 API over mTLS; `SwishMockPaymentProvider` keeps the two simulate buttons. Pure formatting and mapping rules live in `NDSTK.Domain` with xUnit tests.

**Tech Stack:** .NET 10, Umbraco 18.1.1, NPoco over Umbraco scopes, `IHttpClientFactory` with a client certificate, System.Text.Json, xUnit, vanilla JavaScript.

**Spec:** `docs/superpowers/specs/2026-09-02-swish-payment-design.md`

## Global Constraints

- **Never start the site.** Carl runs it and relaunches it himself. While it runs, verify the web project with `dotnet build -t:"ResolveReferences;CoreCompile"` from the repo root, which type-checks without linking. `-t:CoreCompile` alone is not enough once `NDSTK.Domain` changed.
- Tests: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`. They reference `NDSTK.Domain` only. Never add a web or Umbraco reference to `NDSTK.Domain` or `NDSTK.Tests`.
- Money is **integer öre** everywhere. Swish wants `"150.00"`; the conversion happens in `SwishRequest.Amount` and nowhere else.
- Raw SQL must run on **both SQLite and SQL Server**. No `IF NOT EXISTS` on `CREATE INDEX`; ask `BookingSchemaSql.IndexExistsQuery` first. Pass dates as parameters, never formatted into SQL. Use `BookingDialect.Of(Database)` in migrations.
- Swish constraints, verbatim from the spec: instruction id 32 upper-case hex, no hyphens; `payeePaymentReference` 1–35 chars `a-z A-Z 0-9 -`; `message` ≤ 50 chars from letters a-ö A-Ö, digits, space and `:;.,?!()"`; `callbackIdentifier` 32–36 chars `0-9 a-z A-Z -`; amount string with two decimals; currency `SEK`.
- Member-facing copy is **Swedish**. Log messages are English.
- **Never log** the payment request token or the callback identifier. The instruction id may be logged.
- Commit messages: imperative subject line in this repo's style (no `feat:` prefix), body optional, ending with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Umbraco API surface must be **verified by compiling**, not by reading docs. Everything named below was checked against the 18.1.1 assemblies: `IUmbracoBuilder.Config`, `SurfaceController.RedirectToCurrentUmbracoPage`, `Url.SurfaceAction<T>()`, `IOptions<WebRoutingSettings>`, attribute-routed controllers being mapped (proved by `MemberAdminController`).
- Reservation default is **7 minutes**.

---

## File structure

**NDSTK.Domain** (pure, tested)
- `NDSTK.Domain/PaymentStatus.cs` — moved here from `Booking/Data`, so `SwishOutcome` can name the statuses.
- `NDSTK.Domain/SwishRequest.cs` — formatting: instruction id, reference, amount, message, callback identifier, app link.
- `NDSTK.Domain/SwishOutcome.cs` — `Resolve(status, errorCode)` → `PaymentResolution`.
- `NDSTK.Domain/BookingSchemaSql.cs` — three new statement builders.

**Booking/Payments** (provider boundary)
- `Booking/Payments/IPaymentProvider.cs` — widened interface.
- `Booking/Payments/PaymentModels.cs` — `PaymentStart`, `PaymentStartContext`, `PaymentOutcome`, `ProviderStatus`, `PaymentProviderException`.
- `Booking/Payments/SwishMockPaymentProvider.cs` — implements the new members trivially.
- `Booking/Payments/PaymentProviderFactory.cs` — picks Swish or mock from options and certificate.
- `Booking/Payments/PaymentProviderAnnouncer.cs` — logs the active provider at startup.
- `Booking/Payments/Swish/SwishOptions.cs` — bound from `NDSTK:Swish`.
- `Booking/Payments/Swish/SwishCertificateLoader.cs` — PKCS#12 file or machine store.
- `Booking/Payments/Swish/SwishHttpClients.cs` — named clients `swish` (with certificate) and `swish-qr`.
- `Booking/Payments/Swish/SwishPaymentProvider.cs` — the v2 API calls.
- `Booking/Payments/Swish/SwishApiModels.cs` — request and response DTOs.
- `Booking/Payments/Swish/SwishQrService.cs` — fetches and caches the QR image.
- `Booking/Payments/Swish/SwishCallbackUrl.cs` — builds the callback URL from `WebRoutingSettings`.

**Booking/Data**
- `Booking/Data/PaymentRecord.cs` — seven new nullable columns.
- `Booking/Data/BookingTables.cs` — the new index name.
- `Booking/Data/Migrations/AddSwishColumns.cs` — step `swish-1`.
- `Booking/Data/Migrations/BookingMigrationPlan.cs` — one line.
- `Booking/Data/IBookingRepository.cs`, `Booking/Data/BookingRepository.cs` — conditional settlement and the new reads and writes.

**Booking/Services**
- `Booking/Services/BookingService.cs` — `SettlePaymentAsync` idempotent with the late-payment rule; new `StartPaymentAsync`, `ReconcileAsync`, `CancelPaymentAsync`.
- `Booking/Services/MembershipSettings.cs` — default 7.

**Booking/Web**
- `Booking/Web/PaymentPageUrl.cs` — the one place that builds `/medlem/betalning?ref=`.
- `Booking/Web/SwishPaymentSurfaceController.cs` — `Start`, `Status`, `Qr`, `Cancel`; simulate actions gated.
- `Booking/Web/SwishPaymentController.cs` — view model with the started state.
- `Booking/Web/SwishCallbackController.cs` — anonymous callback endpoint.
- `Booking/Web/BookingRateLimits.cs`, `Program.cs` — two new policies.
- `Booking/Jobs/ClassReminderJob.cs` — reconcile step before the sweep.
- `Booking/Admin/MemberAdminDetail.cs`, `Booking/Admin/MemberAdminQueries.cs` — two columns on the payment row.

**Front end**
- `Views/SwishPayment.cshtml` — four states.
- `wwwroot/static/js/swish-payment.js` — device switch and poll.
- `wwwroot/static/css/site.css` — a few `.swish__*` additions.
- `App_Plugins/NDSTK.MemberAdmin/members-dashboard.js`, `lang/sv.js`, `lang/en.js` — one column.

**Config and docs**
- `appsettings.json`, `appsettings.Development.json`, `ContentModel/NdstkContentModelInstaller.cs:92`, `README.md`.

---

## Phase 1 — Foundations

### Task 1: Move `PaymentStatus` into the Domain project

**Files:**
- Move: `Booking/Data/PaymentStatus.cs` → `NDSTK.Domain/PaymentStatus.cs`
- Modify: `Booking/Data/PaymentRecord.cs:1-5`, `Booking/Web/SwishPaymentSurfaceController.cs:1-18`, `Booking/Web/SwishPaymentController.cs:1-8`, `Booking/Admin/MemberAdminQueries.cs:1-12`

**Interfaces:**
- Produces: `NDSTK.Booking.Domain.PaymentStatus` with the unchanged constants `Pending`, `Paid`, `Failed`, `Cancelled`.

- [ ] **Step 1: Move the file and change its namespace**

```bash
git mv Booking/Data/PaymentStatus.cs NDSTK.Domain/PaymentStatus.cs
```

Then edit `NDSTK.Domain/PaymentStatus.cs` so it reads:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Payment statuses, as readable strings for the same reason as <see cref="BookingStatus"/>: a
/// human opening the SQLite file should be able to read it. In the Domain project so that
/// <see cref="SwishOutcome"/> can name them without a dependency on the web assembly.
/// </summary>
public static class PaymentStatus
{
    /// <summary>Created, waiting for the member to complete the Swish step.</summary>
    public const string Pending = "Pending";

    /// <summary>Completed. The booking it belongs to is confirmed.</summary>
    public const string Paid = "Paid";

    /// <summary>Swish reported an error: declined by the bank, timed out, BankID cancelled.</summary>
    public const string Failed = "Failed";

    /// <summary>The member abandoned it, declined it in the app, or the hold ran out.</summary>
    public const string Cancelled = "Cancelled";
}
```

- [ ] **Step 2: Type-check the web project**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: errors `CS0103: The name 'PaymentStatus' does not exist` in files that lack `using NDSTK.Booking.Domain;`.

- [ ] **Step 3: Add the missing using to each failing file**

Add `using NDSTK.Booking.Domain;` to the top of every file the build named. Expected set: `Booking/Data/PaymentRecord.cs`, `Booking/Web/SwishPaymentSurfaceController.cs`, `Booking/Admin/MemberAdminQueries.cs`. `BookingService.cs`, `BookingRepository.cs`, `SwishPaymentController.cs` and `FamilyUpgradeSurfaceController.cs` already have it.

- [ ] **Step 4: Type-check and run the tests**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"` then `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: build succeeds; all existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A NDSTK.Domain/PaymentStatus.cs Booking/Data/PaymentStatus.cs Booking/Data/PaymentRecord.cs Booking/Web/SwishPaymentSurfaceController.cs Booking/Admin/MemberAdminQueries.cs
git commit -m "Move PaymentStatus into the Domain project

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `SwishRequest` formatting rules

**Files:**
- Create: `NDSTK.Domain/SwishRequest.cs`
- Test: `NDSTK.Tests/SwishRequestTests.cs`

**Interfaces:**
- Produces:
  - `string SwishRequest.InstructionId(Guid reference)`
  - `string SwishRequest.PaymentReference(Guid reference)`
  - `string SwishRequest.Amount(int ore)`
  - `string SwishRequest.Message(string? classTitle, DateTime? classStartSwedish)`
  - `string SwishRequest.CallbackIdentifier()`
  - `string SwishRequest.AppLink(string token, string returnUrl)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Globalization;
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// What Swish accepts on a payment request. Each rule is a way a payment can be rejected with a
/// 422 that nothing in the booking logic would ever notice, so each is pinned here.
/// </summary>
public class SwishRequestTests
{
    private static readonly Guid Reference = new("3f2504e0-4f89-41d3-9a0c-0305e82c3301");

    [Fact]
    public void Instruction_id_is_32_upper_case_hex_digits_without_hyphens()
    {
        var id = SwishRequest.InstructionId(Reference);

        Assert.Equal("3F2504E04F8941D39A0C0305E82C3301", id);
        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9A-F]{32}$", id);
    }

    [Fact]
    public void Payment_reference_is_the_same_value_and_fits_the_35_alphanumeric_limit()
    {
        var reference = SwishRequest.PaymentReference(Reference);

        Assert.Equal(SwishRequest.InstructionId(Reference), reference);
        Assert.InRange(reference.Length, 1, 35);
        Assert.Matches("^[a-zA-Z0-9-]+$", reference);
    }

    [Theory]
    [InlineData(15_000, "150.00")]
    [InlineData(5, "0.05")]
    [InlineData(25_050, "250.50")]
    [InlineData(100, "1.00")]
    public void Amount_has_two_decimals_and_a_period(int ore, string expected)
        => Assert.Equal(expected, SwishRequest.Amount(ore));

    [Fact]
    public void Amount_ignores_the_thread_culture()
    {
        // sv-SE would write 150,00. Swish rejects a comma.
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            Assert.Equal("150.00", SwishRequest.Amount(15_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Message_for_a_class_names_the_day_and_time_in_swedish()
    {
        var message = SwishRequest.Message("Minitennis", new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.Equal("Träning 12 september 18:00", message);
    }

    [Fact]
    public void Message_without_a_class_is_the_family_upgrade()
        => Assert.Equal("Familjekonto", SwishRequest.Message(null, null));

    [Fact]
    public void Message_for_a_class_with_no_start_is_just_traning()
        => Assert.Equal("Träning", SwishRequest.Message("Minitennis", null));

    [Fact]
    public void Message_never_exceeds_fifty_characters()
    {
        var message = SwishRequest.Message(new string('x', 200), new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.True(message.Length <= 50, $"was {message.Length}");
    }

    [Fact]
    public void Message_contains_only_characters_swish_allows()
    {
        var message = SwishRequest.Message("Tävling – 6–8 år & mer", new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.Matches("^[a-zA-ZåäöÅÄÖ0-9 :;.,?!()\"]*$", message);
        Assert.DoesNotContain("–", message);
        Assert.DoesNotContain("&", message);
    }

    [Fact]
    public void Callback_identifier_is_32_hex_digits_and_fresh_each_time()
    {
        var first = SwishRequest.CallbackIdentifier();
        var second = SwishRequest.CallbackIdentifier();

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void App_link_carries_the_token_verbatim_and_encodes_the_return_url_once()
    {
        var link = SwishRequest.AppLink(
            "c28a4061470f4af48973bd2a4642b4fa",
            "https://ndstk.se/medlem/betalning?ref=3f2504e0-4f89-41d3-9a0c-0305e82c3301");

        Assert.Equal(
            "swish://paymentrequest?token=c28a4061470f4af48973bd2a4642b4fa"
            + "&callbackurl=https%3A%2F%2Fndstk.se%2Fmedlem%2Fbetalning%3Fref%3D3f2504e0-4f89-41d3-9a0c-0305e82c3301",
            link);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter SwishRequestTests`
Expected: build error `CS0103: The name 'SwishRequest' does not exist`.

- [ ] **Step 3: Implement `SwishRequest`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace NDSTK.Booking.Domain;

/// <summary>
/// The values a Swish payment request is built from, formatted the way Swish validates them.
/// </summary>
/// <remarks>
/// Every method here corresponds to a 422 Swish would otherwise answer with: FF08 for a bad
/// reference, PA02 for a bad amount, RP02 for a bad message. None of that is visible to the
/// booking rules, so the formatting is pinned by tests instead.
/// </remarks>
public static partial class SwishRequest
{
    /// <summary>Swish caps the message at fifty characters.</summary>
    public const int MessageMaxLength = 50;

    private static readonly CultureInfo Swedish = new("sv-SE");

    /// <summary>
    /// The identifier under which the request is stored at Swish: 32 upper-case hexadecimal
    /// digits, no hyphens. The payment's own Guid, so the two can always be matched up.
    /// </summary>
    public static string InstructionId(Guid reference)
        => reference.ToString("N").ToUpperInvariant();

    /// <summary>
    /// The merchant reference Swish echoes back. Same value as the instruction id: 32
    /// alphanumerics fit the 1–35 limit and the allowed alphabet.
    /// </summary>
    public static string PaymentReference(Guid reference) => InstructionId(reference);

    /// <summary>"150.00". Invariant culture: a Swedish thread would write a comma.</summary>
    public static string Amount(int ore)
        => (ore / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the member sees in their Swish history. Built from the class rather than typed, so
    /// no title can smuggle in a character Swish rejects - or, against the simulator, an error
    /// code.
    /// </summary>
    public static string Message(string? classTitle, DateTime? classStartSwedish)
    {
        if (classTitle is null)
        {
            return "Familjekonto";
        }

        var text = classStartSwedish is { } start
            ? $"Träning {start.ToString("d MMMM HH:mm", Swedish)}"
            : "Träning";

        return Sanitise(text);
    }

    /// <summary>A fresh value per request. Never logged; it is what authenticates the callback.</summary>
    public static string CallbackIdentifier() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// The URL that opens the Swish app with the request preloaded. The return URL is encoded
    /// exactly once; the app decodes it once before opening it.
    /// </summary>
    public static string AppLink(string token, string returnUrl)
        => $"swish://paymentrequest?token={token}&callbackurl={Uri.EscapeDataString(returnUrl)}";

    private static string Sanitise(string text)
    {
        var allowed = Disallowed().Replace(text, string.Empty);
        var collapsed = Whitespace().Replace(allowed, " ").Trim();

        return collapsed.Length <= MessageMaxLength
            ? collapsed
            : collapsed[..MessageMaxLength].TrimEnd();
    }

    [GeneratedRegex("[^a-zA-ZåäöÅÄÖ0-9 :;.,?!()\"]")]
    private static partial Regex Disallowed();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter SwishRequestTests`
Expected: 13 passed.

- [ ] **Step 5: Commit**

```bash
git add NDSTK.Domain/SwishRequest.cs NDSTK.Tests/SwishRequestTests.cs
git commit -m "Format Swish payment request values as pure rules

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `SwishOutcome.Resolve`

**Files:**
- Create: `NDSTK.Domain/SwishOutcome.cs`
- Test: `NDSTK.Tests/SwishOutcomeTests.cs`

**Interfaces:**
- Consumes: `PaymentStatus` (Task 1).
- Produces: `PaymentResolution SwishOutcome.Resolve(string status, string? errorCode)`; `record PaymentResolution(bool IsTerminal, string PaymentStatus, string MemberMessage)`; the Swish status constants `SwishOutcome.Created`, `Paid`, `Declined`, `Error`, `Cancelled`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// What each answer from Swish means to this site. The statuses come from the payment request
/// object; the error codes from the integration guide's table.
/// </summary>
public class SwishOutcomeTests
{
    [Fact]
    public void Created_is_not_terminal_and_keeps_the_payment_pending()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("CREATED", null);

        Assert.False(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Pending, resolution.PaymentStatus);
    }

    [Fact]
    public void Paid_is_terminal_and_paid()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("PAID", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Paid, resolution.PaymentStatus);
    }

    [Fact]
    public void Declined_by_the_member_is_cancelled_and_says_so()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("DECLINED", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Cancelled, resolution.PaymentStatus);
        Assert.Contains("avböjde", resolution.MemberMessage);
    }

    [Fact]
    public void Cancelled_is_cancelled()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("CANCELLED", null);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Cancelled, resolution.PaymentStatus);
    }

    [Theory]
    [InlineData("RF07")]
    [InlineData("BANKIDCL")]
    [InlineData("FF10")]
    [InlineData("TM01")]
    [InlineData("DS24")]
    [InlineData("BANKIDONGOING")]
    [InlineData("BANKIDUNKN")]
    public void Every_documented_error_code_is_failed_with_its_own_sentence(string code)
    {
        PaymentResolution resolution = SwishOutcome.Resolve("ERROR", code);

        Assert.True(resolution.IsTerminal);
        Assert.Equal(PaymentStatus.Failed, resolution.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(resolution.MemberMessage));
        Assert.NotEqual(SwishOutcome.Resolve("ERROR", "XXXX").MemberMessage, resolution.MemberMessage);
    }

    [Fact]
    public void Timed_out_names_the_cause_so_the_member_knows_to_be_quicker()
        => Assert.Contains("tid", SwishOutcome.Resolve("ERROR", "TM01").MemberMessage);

    [Fact]
    public void Unknown_outcome_tells_the_member_to_check_swish_before_paying_again()
        => Assert.Contains("Swish-appen", SwishOutcome.Resolve("ERROR", "DS24").MemberMessage);

    [Fact]
    public void An_unknown_error_code_is_still_failed_with_a_generic_sentence()
    {
        PaymentResolution resolution = SwishOutcome.Resolve("ERROR", "ZZ99");

        Assert.Equal(PaymentStatus.Failed, resolution.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(resolution.MemberMessage));
    }

    [Fact]
    public void Status_comparison_ignores_case()
        => Assert.Equal(PaymentStatus.Paid, SwishOutcome.Resolve("paid", null).PaymentStatus);

    [Fact]
    public void An_unknown_status_is_not_terminal()
        => Assert.False(SwishOutcome.Resolve("SOMETHING_NEW", null).IsTerminal);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter SwishOutcomeTests`
Expected: build error, `SwishOutcome` does not exist.

- [ ] **Step 3: Implement `SwishOutcome`**

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// What a Swish status means here: the status to store, whether anything can still change,
/// and the sentence the member reads.
/// </summary>
public sealed record PaymentResolution(bool IsTerminal, string PaymentStatus, string MemberMessage);

/// <summary>
/// Maps the status and error code on a Swish payment request object to this site's terms.
/// </summary>
/// <remarks>
/// The error codes are the ones the integration guide lists for a payment request. An unknown
/// code is still a failure - Swish said ERROR - it just gets a sentence that does not guess.
/// </remarks>
public static class SwishOutcome
{
    public const string Created = "CREATED";
    public const string Paid = "PAID";
    public const string Declined = "DECLINED";
    public const string Error = "ERROR";
    public const string Cancelled = "CANCELLED";

    private const string GenericFailure =
        "Betalningen gick inte igenom. Platsen är inte bokad. Försök igen, eller kontakta oss på "
        + "info@ndstk.se om det upprepas.";

    public static PaymentResolution Resolve(string status, string? errorCode)
    {
        switch (status.ToUpperInvariant())
        {
            case Paid:
                return new PaymentResolution(true, Domain.PaymentStatus.Paid, "Klart! Betalningen är genomförd.");

            case Declined:
                return new PaymentResolution(
                    true, Domain.PaymentStatus.Cancelled, "Du avböjde betalningen i Swish. Platsen är inte bokad.");

            case Cancelled:
                return new PaymentResolution(
                    true, Domain.PaymentStatus.Cancelled, "Betalningen avbröts. Platsen är inte bokad.");

            case Error:
                return new PaymentResolution(true, Domain.PaymentStatus.Failed, FailureMessage(errorCode));

            default:
                // CREATED, and anything Swish adds later: nothing has been decided.
                return new PaymentResolution(false, Domain.PaymentStatus.Pending, "Väntar på Swish.");
        }
    }

    private static string FailureMessage(string? errorCode) => errorCode?.ToUpperInvariant() switch
    {
        "RF07" => "Banken nekade betalningen. Platsen är inte bokad. Kontrollera din Swish-gräns med banken.",
        "BANKIDCL" => "Signeringen med BankID avbröts, så betalningen genomfördes inte. Platsen är inte bokad.",
        "BANKIDONGOING" => "BankID var upptaget med något annat. Avsluta det och försök igen.",
        "BANKIDUNKN" => "BankID kunde inte godkänna betalningen. Platsen är inte bokad.",
        "FF10" => "Ett fel uppstod hos banken. Platsen är inte bokad. Försök igen om en liten stund.",
        "TM01" => "Betalningen hann inte godkännas i tid. Platsen är inte bokad. Boka igen och öppna Swish direkt.",
        "DS24" => "Swish fick inget svar från banken, så det är oklart om pengarna drogs. Kontrollera i "
                  + "Swish-appen innan du försöker igen, och kontakta oss på info@ndstk.se om du blivit debiterad.",
        _ => GenericFailure,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter SwishOutcomeTests`
Expected: all pass (16 test cases).

- [ ] **Step 5: Commit**

```bash
git add NDSTK.Domain/SwishOutcome.cs NDSTK.Tests/SwishOutcomeTests.cs
git commit -m "Map Swish statuses and error codes to payment outcomes

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Schema statements for the new columns and the filtered index

**Files:**
- Modify: `NDSTK.Domain/BookingSchemaSql.cs`
- Test: `NDSTK.Tests/BookingSchemaSqlTests.cs`

**Interfaces:**
- Produces:
  - `string BookingSchemaSql.AddNullableStringColumn(SqlDialect dialect, string table, string column, int length)`
  - `string BookingSchemaSql.AddNullableDateTimeColumn(SqlDialect dialect, string table, string column)`
  - `string BookingSchemaSql.CreateFilteredUniqueIndex(string indexName, string table, string column)`

- [ ] **Step 1: Add the failing tests to `BookingSchemaSqlTests`**

Add inside the class, after `An_integer_column_carries_its_default_so_existing_rows_stay_valid`:

```csharp
    [Fact]
    public void A_nullable_string_column_uses_nvarchar_on_sql_server_and_text_on_sqlite()
    {
        Assert.Equal(
            $"ALTER TABLE {Payment} ADD ProviderReference nvarchar(36) NULL",
            BookingSchemaSql.AddNullableStringColumn(SqlDialect.SqlServer, Payment, "ProviderReference", 36));

        Assert.Equal(
            $"ALTER TABLE {Payment} ADD COLUMN ProviderReference TEXT NULL",
            BookingSchemaSql.AddNullableStringColumn(SqlDialect.Sqlite, Payment, "ProviderReference", 36));
    }

    [Fact]
    public void A_nullable_datetime_column_matches_the_types_umbraco_already_used()
    {
        Assert.Equal(
            $"ALTER TABLE {Payment} ADD StartedUtc datetime NULL",
            BookingSchemaSql.AddNullableDateTimeColumn(SqlDialect.SqlServer, Payment, "StartedUtc"));

        Assert.Equal(
            $"ALTER TABLE {Payment} ADD COLUMN StartedUtc TEXT NULL",
            BookingSchemaSql.AddNullableDateTimeColumn(SqlDialect.Sqlite, Payment, "StartedUtc"));
    }

    [Fact]
    public void The_filtered_unique_index_excludes_nulls_and_never_says_IF_NOT_EXISTS()
    {
        var sql = BookingSchemaSql.CreateFilteredUniqueIndex(
            "IX_ndstkPayment_ProviderReference", Payment, "ProviderReference");

        Assert.Equal(
            $"CREATE UNIQUE INDEX IX_ndstkPayment_ProviderReference ON {Payment} (ProviderReference) "
            + "WHERE ProviderReference IS NOT NULL",
            sql);
        Assert.DoesNotContain("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_filtered_unique_index_lets_many_unstarted_payments_coexist_but_not_two_of_one_request()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Execute(connection,
            $"CREATE TABLE {Payment} (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProviderReference TEXT NULL)");
        Execute(connection, BookingSchemaSql.CreateFilteredUniqueIndex(
            "IX_ndstkPayment_ProviderReference", Payment, "ProviderReference"));

        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES (NULL)");
        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES (NULL)");
        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES ('ABC')");

        SqliteException failure = Assert.Throws<SqliteException>(
            () => Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES ('ABC')"));

        Assert.Contains("UNIQUE", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter BookingSchemaSqlTests`
Expected: build errors naming the three new methods.

- [ ] **Step 3: Implement the three builders**

In `NDSTK.Domain/BookingSchemaSql.cs`, after `AddIntegerColumn`:

```csharp
    /// <summary>
    /// A nullable text column of bounded length. SQL Server gets the length; SQLite has no
    /// bounded text type and takes TEXT, which is also what NPoco reads a string back from.
    /// </summary>
    public static string AddNullableStringColumn(SqlDialect dialect, string table, string column, int length)
        => AddColumn(dialect, table, column,
            dialect is SqlDialect.SqlServer ? $"nvarchar({length}) NULL" : "TEXT NULL");

    /// <summary>
    /// A nullable datetime. Umbraco's own syntax providers created the existing date columns as
    /// datetime on SQL Server and TEXT on SQLite, and NPoco formats every value it writes the
    /// same way for both, so the new columns sort and compare like the old ones.
    /// </summary>
    public static string AddNullableDateTimeColumn(SqlDialect dialect, string table, string column)
        => AddColumn(dialect, table, column,
            dialect is SqlDialect.SqlServer ? "datetime NULL" : "TEXT NULL");

    /// <summary>
    /// Unique among the rows that have a value. Without the filter SQL Server treats every NULL
    /// as the same value and refuses the second payment that has not started; SQLite would
    /// accept it, and the two engines would enforce different rules. Both accept this statement
    /// verbatim. No IF NOT EXISTS, for the reason <see cref="CreateLiveBookingIndex"/> gives.
    /// </summary>
    public static string CreateFilteredUniqueIndex(string indexName, string table, string column)
        => $"CREATE UNIQUE INDEX {indexName} ON {table} ({column}) WHERE {column} IS NOT NULL";
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter BookingSchemaSqlTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add NDSTK.Domain/BookingSchemaSql.cs NDSTK.Tests/BookingSchemaSqlTests.cs
git commit -m "Add schema statements for nullable text, datetime and a filtered unique index

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Swish columns on `ndstkPayment`

**Files:**
- Modify: `Booking/Data/PaymentRecord.cs` (after the `Provider` property, line 57)
- Modify: `Booking/Data/BookingTables.cs`
- Create: `Booking/Data/Migrations/AddSwishColumns.cs`
- Modify: `Booking/Data/Migrations/BookingMigrationPlan.cs`

**Interfaces:**
- Consumes: Task 4's three builders.
- Produces: `PaymentRecord.ProviderReference`, `ProviderToken`, `CallbackIdentifier`, `BankReference`, `ErrorCode` (all `string?`), `StartedUtc`, `LastCheckedUtc` (both `DateTime?`); `BookingTables.PaymentProviderReferenceIndex`.

- [ ] **Step 1: Add the columns to `PaymentRecord`**

Insert after the `Provider` property:

```csharp
    /// <summary>
    /// Swish's identifier for the request: the instruction UUID we chose, 32 upper-case hex digits.
    /// Null until the member starts the payment. What the callback names.
    /// </summary>
    [Column(nameof(ProviderReference))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(36)]
    public string? ProviderReference { get; set; }

    /// <summary>
    /// The PaymentRequestToken Swish returned. Opens the app and draws the QR, so the page needs
    /// it again on every reload while the payment is pending. Never logged.
    /// </summary>
    [Column(nameof(ProviderToken))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(64)]
    public string? ProviderToken { get; set; }

    /// <summary>
    /// Sent to Swish and echoed back as a header on the callback. The only way to tell a callback
    /// from Swish apart from one anybody could POST. Never logged.
    /// </summary>
    [Column(nameof(CallbackIdentifier))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(36)]
    public string? CallbackIdentifier { get; set; }

    /// <summary>Swish's paymentReference once PAID: what the bank statement shows.</summary>
    [Column(nameof(BankReference))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(64)]
    public string? BankReference { get; set; }

    /// <summary>Swish's errorCode when the status is Failed.</summary>
    [Column(nameof(ErrorCode))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(20)]
    public string? ErrorCode { get; set; }

    /// <summary>When the request was created at Swish. Reconciliation waits a minute from here.</summary>
    [Column(nameof(StartedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? StartedUtc { get; set; }

    /// <summary>Last time Swish was asked about this request, so the poll does not ask every second.</summary>
    [Column(nameof(LastCheckedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastCheckedUtc { get; set; }
```

Do **not** put an `[Index]` attribute on `ProviderReference`. `Create.Table<PaymentRecord>()` would turn it into an unfiltered unique index on a fresh install, which SQL Server would then enforce across NULLs. The migration below creates the filtered one on every database, fresh or not.

- [ ] **Step 2: Name the index in `BookingTables`**

Add after `LivePerParticipantIndex`:

```csharp
    /// <summary>One row per Swish request; rows that have not started a payment are excluded.</summary>
    internal const string PaymentProviderReferenceIndex = "IX_ndstkPayment_ProviderReference";
```

- [ ] **Step 3: Write the migration**

```csharp
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// The columns a real Swish payment leaves behind on the payment row, and the index the callback
/// looks a payment up by.
/// </summary>
/// <remarks>
/// Every column is nullable: rows from before this step, and rows the member never starts a
/// payment on, have nothing to put in them. The index is filtered to rows that have a value,
/// because SQL Server treats NULLs as equal in a unique index and would refuse the second unstarted
/// payment. Created here rather than by an attribute on the POCO so a fresh install gets the same
/// filtered index as an upgraded one.
/// </remarks>
internal sealed class AddSwishColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        SqlDialect dialect = BookingDialect.Of(Database);
        var table = BookingTables.Payment;

        AddColumnIfMissing(table, "ProviderReference",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ProviderReference", 36));
        AddColumnIfMissing(table, "ProviderToken",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ProviderToken", 64));
        AddColumnIfMissing(table, "CallbackIdentifier",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "CallbackIdentifier", 36));
        AddColumnIfMissing(table, "BankReference",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "BankReference", 64));
        AddColumnIfMissing(table, "ErrorCode",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ErrorCode", 20));
        AddColumnIfMissing(table, "StartedUtc",
            BookingSchemaSql.AddNullableDateTimeColumn(dialect, table, "StartedUtc"));
        AddColumnIfMissing(table, "LastCheckedUtc",
            BookingSchemaSql.AddNullableDateTimeColumn(dialect, table, "LastCheckedUtc"));

        CreateIndexIfMissing(dialect);

        return Task.CompletedTask;
    }

    private void AddColumnIfMissing(string table, string column, string sql)
    {
        if (ColumnExists(table, column))
        {
            Logger.LogDebug("Column {Table}.{Column} already exists; skipping.", table, column);
            return;
        }

        Database.Execute(sql);
        Logger.LogInformation("Added column {Table}.{Column}.", table, column);
    }

    private void CreateIndexIfMissing(SqlDialect dialect)
    {
        var exists = Database.ExecuteScalar<int>(
            BookingSchemaSql.IndexExistsQuery(dialect), BookingTables.PaymentProviderReferenceIndex) > 0;

        if (exists)
        {
            Logger.LogDebug(
                "Index {IndexName} already exists; skipping.", BookingTables.PaymentProviderReferenceIndex);
            return;
        }

        Database.Execute(BookingSchemaSql.CreateFilteredUniqueIndex(
            BookingTables.PaymentProviderReferenceIndex, BookingTables.Payment, "ProviderReference"));

        Logger.LogInformation("Created index {IndexName}.", BookingTables.PaymentProviderReferenceIndex);
    }
}
```

- [ ] **Step 4: Append the step to the plan**

`Booking/Data/Migrations/BookingMigrationPlan.cs`:

```csharp
    public BookingMigrationPlan() : base("NDSTK.Booking")
        => From(string.Empty)
            .To<AddBookingTables>("booking-tables-1")
            .To<AddParticipantTable>("participants-1")
            .To<AddSwishColumns>("swish-1");
```

- [ ] **Step 5: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add Booking/Data/PaymentRecord.cs Booking/Data/BookingTables.cs Booking/Data/Migrations/AddSwishColumns.cs Booking/Data/Migrations/BookingMigrationPlan.cs
git commit -m "Give the payment row columns for Swish's identifiers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint for Carl (next relaunch):** the log shows seven `Added column ndstkPayment.…` lines and `Created index IX_ndstkPayment_ProviderReference`, then nothing on the relaunch after.

---

### Task 6: Widen `IPaymentProvider`; the mock implements it

**Files:**
- Modify: `Booking/Payments/IPaymentProvider.cs`
- Create: `Booking/Payments/PaymentModels.cs`
- Modify: `Booking/Payments/SwishMockPaymentProvider.cs`

**Interfaces:**
- Produces:

```csharp
public interface IPaymentProvider
{
    string Name { get; }
    Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context);
    Task<PaymentOutcome> RetrieveAsync(string providerReference);
    Task<PaymentOutcome> CancelAsync(string providerReference);
}
public sealed record PaymentStartContext(string CallbackUrl, string Message);
public sealed record PaymentStart(string ProviderReference, string? Token, string CallbackIdentifier);
public sealed record PaymentOutcome(ProviderStatus Status, string? BankReference, string? ErrorCode, DateTime? PaidUtc);
public enum ProviderStatus { Created, Paid, Declined, Error, Cancelled }
public sealed class PaymentProviderException : Exception { public string? ErrorCode { get; } }
```

- [ ] **Step 1: Replace `IPaymentProvider.cs`**

```csharp
using NDSTK.Booking.Data;

namespace NDSTK.Booking.Payments;

/// <summary>
/// How the club takes money. The booking logic talks to this and nothing else, so the mock and
/// Swish are interchangeable: <c>BookingComposer</c> picks one from configuration.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Recorded on the payment row, so a real payment is distinguishable from a mock.</summary>
    string Name { get; }

    /// <summary>
    /// Creates the request at the provider. Returns what the page needs to hand the member over.
    /// Throws <see cref="PaymentProviderException"/> when the provider refuses or cannot be reached;
    /// the caller leaves the payment untouched so the member can try again.
    /// </summary>
    Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context);

    /// <summary>
    /// Asks the provider what happened. A terminal answer is returned, never thrown. Throws only
    /// when the provider cannot be reached, so a caller can tell "declined" from "unknown".
    /// </summary>
    Task<PaymentOutcome> RetrieveAsync(string providerReference);

    /// <summary>
    /// Withdraws a request the member has not answered. A request that is already final is
    /// reported as its final state rather than as a failure.
    /// </summary>
    Task<PaymentOutcome> CancelAsync(string providerReference);
}
```

- [ ] **Step 2: Create `PaymentModels.cs`**

```csharp
namespace NDSTK.Booking.Payments;

/// <summary>What the provider needs beyond the payment row itself.</summary>
public sealed record PaymentStartContext(string CallbackUrl, string Message);

/// <summary>
/// What starting a payment produced. <paramref name="Token"/> is the value that opens the Swish
/// app and draws the QR code; the mock has none worth the name.
/// </summary>
public sealed record PaymentStart(string ProviderReference, string? Token, string CallbackIdentifier);

/// <summary>Where a request stands at the provider. Terminal unless <see cref="Status"/> is Created.</summary>
public sealed record PaymentOutcome(
    ProviderStatus Status, string? BankReference, string? ErrorCode, DateTime? PaidUtc)
{
    public bool IsTerminal => Status != ProviderStatus.Created;
}

public enum ProviderStatus
{
    Created,
    Paid,
    Declined,
    Error,
    Cancelled,
}

/// <summary>
/// The provider refused or could not be reached. <see cref="ErrorCode"/> is Swish's code when
/// there was one (a 422), null for a transport failure.
/// </summary>
public sealed class PaymentProviderException(string message, string? errorCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string? ErrorCode { get; } = errorCode;
}
```

- [ ] **Step 3: Make the mock implement the new members**

Replace `SwishMockPaymentProvider.cs`:

```csharp
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Stands in for Swish when no certificate is configured.
/// </summary>
/// <remarks>
/// Starting a payment invents a reference and a token so the page reaches its "started" state.
/// Retrieving always answers Created: the mock has no app for anyone to approve in, so the two
/// simulate buttons settle the payment directly through <c>BookingService</c> instead, exactly as
/// they always have. That keeps this class free of any database dependency, which matters
/// because the provider is a singleton and the repository is scoped.
/// </remarks>
public sealed class SwishMockPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "SwishMock";

    public string Name => ProviderName;

    public Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context)
        => Task.FromResult(new PaymentStart(
            SwishRequest.InstructionId(payment.Reference),
            Token: "mock",
            SwishRequest.CallbackIdentifier()));

    public Task<PaymentOutcome> RetrieveAsync(string providerReference)
        => Task.FromResult(new PaymentOutcome(ProviderStatus.Created, null, null, null));

    public Task<PaymentOutcome> CancelAsync(string providerReference)
        => Task.FromResult(new PaymentOutcome(ProviderStatus.Cancelled, null, null, null));
}
```

- [ ] **Step 4: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success. `BookingService` and `FamilyUpgradeSurfaceController` only use `Name`, so nothing else changes.

- [ ] **Step 5: Commit**

```bash
git add Booking/Payments/IPaymentProvider.cs Booking/Payments/PaymentModels.cs Booking/Payments/SwishMockPaymentProvider.cs
git commit -m "Give the payment provider start, retrieve and cancel

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Conditional settlement, the late-payment rule, and the new repository methods

**Files:**
- Modify: `Booking/Data/IBookingRepository.cs` (replace `CompletePaymentAsync` at line 85; add methods)
- Modify: `Booking/Data/BookingRepository.cs` (`CompletePaymentAsync` at lines 307-314; `ExpireBookingAsync` at 316-339; add methods after `GetPaymentByReferenceAsync`)
- Modify: `Booking/Services/BookingService.cs` (`SettlePaymentAsync`, `AbandonPaymentAsync`)
- Modify: `Booking/Web/SwishPaymentSurfaceController.cs` (the two simulate actions call the changed signatures)

**Interfaces:**
- Consumes: `PaymentRecord` columns (Task 5), `PaymentOutcome` (Task 6).
- Produces on `IBookingRepository`:

```csharp
Task<PaymentRecord?> GetPaymentByProviderReferenceAsync(string providerReference);
Task<bool> TryStartPaymentAsync(int paymentId, string providerReference, string? token, string callbackIdentifier, DateTime nowUtc);
Task<bool> TryRestartHoldAsync(int bookingId, DateTime holdExpiresUtc);
Task<bool> TryCompletePaymentAsync(int paymentId, string status, DateTime nowUtc, string? bankReference, string? errorCode);
Task StampPaymentCheckedAsync(int paymentId, DateTime nowUtc);
Task<IReadOnlyList<PaymentRecord>> GetPaymentsAwaitingReconciliationAsync(DateTime startedBeforeUtc);
Task<bool> TryReconfirmBookingAsync(int bookingId, int capacity, DateTime nowUtc);
Task IssueCreditAsync(Guid memberKey, int sourceBookingId, DateTime nowUtc);
```

- Produces on `BookingService`:

```csharp
public enum SettlementResult { AlreadySettled, Confirmed, Reconfirmed, Credited, NoBooking }
Task<SettlementResult> SettlePaymentAsync(PaymentRecord payment, string? bankReference = null);
Task<bool> AbandonPaymentAsync(PaymentRecord payment, string status, string? errorCode = null);
```

- [ ] **Step 1: Change the interface**

In `IBookingRepository.cs`, replace

```csharp
    Task CompletePaymentAsync(int paymentId, string status, DateTime nowUtc);
```

with

```csharp
    /// <summary>
    /// Moves a payment out of Pending, and only out of Pending. Returns false when it already
    /// left, which is how the callback, the page's poll and the job agree on exactly one winner.
    /// </summary>
    Task<bool> TryCompletePaymentAsync(
        int paymentId, string status, DateTime nowUtc, string? bankReference, string? errorCode);

    /// <summary>The payment a Swish callback names. Null for a reference nobody started.</summary>
    Task<PaymentRecord?> GetPaymentByProviderReferenceAsync(string providerReference);

    /// <summary>
    /// Records that a request exists at the provider. Conditional on none existing yet, so two
    /// tabs pressing Betala at once create one request, not two.
    /// </summary>
    Task<bool> TryStartPaymentAsync(
        int paymentId, string providerReference, string? token, string callbackIdentifier, DateTime nowUtc);

    /// <summary>
    /// Restarts the reservation clock when the payment starts, so the hold outlives Swish's own
    /// timeout however long the member looked at the page first. Pending bookings only.
    /// </summary>
    Task<bool> TryRestartHoldAsync(int bookingId, DateTime holdExpiresUtc);

    /// <summary>Notes that Swish was just asked, so the next poll waits its turn.</summary>
    Task StampPaymentCheckedAsync(int paymentId, DateTime nowUtc);

    /// <summary>Pending payments with a request at the provider, started before the given time.</summary>
    Task<IReadOnlyList<PaymentRecord>> GetPaymentsAwaitingReconciliationAsync(DateTime startedBeforeUtc);

    /// <summary>
    /// Gives an expired booking its place back, if the class still has room for it. The capacity
    /// test is in the WHERE clause, like the reservation's, so it cannot overbook. False when
    /// the class is full, or the child has since taken another live place on it.
    /// </summary>
    Task<bool> TryReconfirmBookingAsync(int bookingId, int capacity, DateTime nowUtc);

    /// <summary>One credit, as a cancellation would issue it.</summary>
    Task IssueCreditAsync(Guid memberKey, int sourceBookingId, DateTime nowUtc);
```

- [ ] **Step 2: Implement them in `BookingRepository`**

Replace `CompletePaymentAsync` (lines 307-314) with:

```csharp
    public async Task<bool> TryCompletePaymentAsync(
        int paymentId, string status, DateTime nowUtc, string? bankReference, string? errorCode)
    {
        using IScope scope = scopeProvider.CreateScope();

        // "Still pending" is in the WHERE clause. Swish retries its callback, the page polls, and
        // the job reconciles - all three can carry the same PAID within a second. One updates a
        // row; the others update none and do nothing further.
        var updated = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Payment}
            SET Status = @0, CompletedUtc = @1, BankReference = @2, ErrorCode = @3
            WHERE Id = @4 AND Status = @5
            """,
            status, nowUtc, bankReference, errorCode, paymentId, PaymentStatus.Pending);

        scope.Complete();
        return updated == 1;
    }

    public async Task<PaymentRecord?> GetPaymentByProviderReferenceAsync(string providerReference)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<PaymentRecord>()
            .From<PaymentRecord>()
            .Where<PaymentRecord>(record => record.ProviderReference == providerReference);

        return await scope.Database.FirstOrDefaultAsync<PaymentRecord>(sql);
    }

    public async Task<bool> TryStartPaymentAsync(
        int paymentId, string providerReference, string? token, string callbackIdentifier, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        var updated = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Payment}
            SET ProviderReference = @0, ProviderToken = @1, CallbackIdentifier = @2, StartedUtc = @3
            WHERE Id = @4 AND Status = @5 AND ProviderReference IS NULL
            """,
            providerReference, token, callbackIdentifier, nowUtc, paymentId, PaymentStatus.Pending);

        scope.Complete();
        return updated == 1;
    }

    public async Task<bool> TryRestartHoldAsync(int bookingId, DateTime holdExpiresUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        var updated = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET HoldExpiresUtc = @0
            WHERE Id = @1 AND Status = @2
            """,
            holdExpiresUtc, bookingId, Domain.BookingStatus.Pending);

        scope.Complete();
        return updated == 1;
    }

    public async Task StampPaymentCheckedAsync(int paymentId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {BookingTables.Payment} SET LastCheckedUtc = @0 WHERE Id = @1", nowUtc, paymentId);
        scope.Complete();
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetPaymentsAwaitingReconciliationAsync(
        DateTime startedBeforeUtc)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<PaymentRecord>()
            .From<PaymentRecord>()
            .Where<PaymentRecord>(record =>
                record.Status == PaymentStatus.Pending
                && record.ProviderReference != null
                && record.StartedUtc != null
                && record.StartedUtc <= startedBeforeUtc)
            .OrderBy<PaymentRecord>(record => record.Id);

        return await scope.Database.FetchAsync<PaymentRecord>(sql);
    }

    public async Task<bool> TryReconfirmBookingAsync(int bookingId, int capacity, DateTime nowUtc)
    {
        if (capacity <= 0)
        {
            return false;
        }

        using IScope scope = scopeProvider.CreateScope();

        try
        {
            // The same counting subquery TryReservePlaceAsync uses, so the two cannot disagree
            // about what "room" means. The row's own class is read inside the statement rather
            // than passed in, so a caller cannot hand it the wrong class.
            var updated = await scope.Database.ExecuteAsync(
                $"""
                UPDATE {BookingTables.Booking}
                SET Status = @0, ConfirmedUtc = @1, HoldExpiresUtc = NULL
                WHERE Id = @2 AND Status = @3
                  AND (
                    SELECT COUNT(*) FROM {BookingTables.Booking} b
                    WHERE b.ClassKey = (SELECT ClassKey FROM {BookingTables.Booking} WHERE Id = @2)
                      AND (b.Status = @0 OR (b.Status = @4 AND b.HoldExpiresUtc > @1))
                  ) < @5
                """,
                Domain.BookingStatus.Confirmed, nowUtc, bookingId, Domain.BookingStatus.Expired,
                Domain.BookingStatus.Pending, capacity);

            scope.Complete();
            return updated == 1;
        }
        catch (DbException exception)
            when (exception.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            // The child took another live place on this class while the payment was in flight,
            // and the one-live-booking index refuses a second. The caller credits them instead.
            logger.LogWarning(
                "Booking {BookingId} could not be re-confirmed: the child already holds a live place.",
                bookingId);

            scope.Complete();
            return false;
        }
    }

    public async Task IssueCreditAsync(Guid memberKey, int sourceBookingId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new CreditRecord
        {
            MemberKey = memberKey,
            SourceBookingId = sourceBookingId,
            IssuedUtc = nowUtc,
        });

        scope.Complete();
    }
```

Then in `ExpireBookingAsync`, after the credit give-back `UPDATE` and before `scope.Complete()`, add:

```csharp
        // A payment nobody started at the provider has nowhere to go once its hold is gone. Marked
        // Cancelled so it stops lingering as Pending in the backoffice. A payment that HAS a
        // request at Swish is left alone: reconciliation must still be able to settle it if the
        // member paid and the callback was lost.
        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Payment}
            SET Status = @0, CompletedUtc = @1
            WHERE BookingId = @2 AND Status = @3 AND ProviderReference IS NULL
            """,
            PaymentStatus.Cancelled, nowUtc, bookingId, PaymentStatus.Pending);
```

- [ ] **Step 3: Type-check to find the callers**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: `CS1061` on `CompletePaymentAsync` in `BookingService.cs` (two places).

- [ ] **Step 4: Rewrite settlement in `BookingService`**

Add the enum above the class, after `BookingAttempt`:

```csharp
/// <summary>What settling a paid payment did about the place it was for.</summary>
public enum SettlementResult
{
    /// <summary>Somebody else settled it first. Nothing was changed.</summary>
    AlreadySettled,

    /// <summary>The pending booking is now confirmed.</summary>
    Confirmed,

    /// <summary>The hold had lapsed, but the class still had room, so the place is theirs.</summary>
    Reconfirmed,

    /// <summary>The hold had lapsed and the class filled. The member has a credit instead.</summary>
    Credited,

    /// <summary>A purchase with no booking attached: the family upgrade.</summary>
    NoBooking,
}
```

Replace `SettlePaymentAsync` with:

```csharp
    /// <summary>
    /// Completes a payment: confirms the booking, extends the membership if the fee was included,
    /// and marks the welcome price used if it was charged.
    /// </summary>
    /// <remarks>
    /// Idempotent. The first statement moves the payment out of Pending conditionally, and every
    /// side effect below runs only when that statement changed a row. Swish's callback, the
    /// page's poll and the reminder job can all arrive with the same PAID; one of them wins.
    /// </remarks>
    public async Task<SettlementResult> SettlePaymentAsync(PaymentRecord payment, string? bankReference = null)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, PaymentStatus.Paid, nowUtc, bankReference, errorCode: null);

        if (won is false)
        {
            logger.LogInformation("Payment {Reference} was already settled; nothing to do.", payment.Reference);
            return SettlementResult.AlreadySettled;
        }

        BookingRecord? booking = payment.BookingId is { } bookingId
            ? await repository.GetBookingAsync(bookingId)
            : null;

        SettlementResult result = booking is null
            ? SettlementResult.NoBooking
            : await PlaceForPaidBookingAsync(booking, payment.MemberKey, nowUtc);

        if (payment.MembershipFeeOre > 0)
        {
            await profiles.ExtendMembershipAsync(payment.MemberKey, today);
        }

        // Deliberately not "did this payment equal the welcome price". Comparing the stored amount
        // against the configured price would break the moment an editor changes prices between a
        // booking and its payment, and would misfire entirely if the two prices were ever set the
        // same. Instead: Pricing only ever quotes the welcome price while the child's stamp is
        // still null, so a class fee charged to a child whose stamp is null *was* the welcome
        // price, whatever the numbers now say.
        //
        // Stamped even when the place became a credit: the welcome price was paid, and the credit
        // is worth a class.
        if (payment.ClassFeeOre > 0 && booking?.ParticipantKey is { } participantKey)
        {
            await participants.TryStampFirstClassUsedAsync(participantKey, nowUtc);
        }

        // The supplement is charged either alongside the annual fee on a renewal, or on its own as
        // a mid-year upgrade. Either way, paying it makes the account a family account. Note that
        // ExtendMembershipAsync above is guarded on MembershipFeeOre, which an upgrade payment sets
        // to zero - that is what stops the upgrade moving the expiry date.
        if (payment.FamilyFeeOre > 0)
        {
            await profiles.SetFamilyAccountAsync(payment.MemberKey);
        }

        logger.LogInformation("Payment {Reference} settled: {Result}.", payment.Reference, result);
        return result;
    }

    /// <summary>
    /// Gives a paid booking its place. Normally the booking is still Pending. When the hold ran out
    /// first - a slow BankID, a lost callback - the place is taken back if the class has room, and
    /// otherwise the member receives a credit, exactly as a cancellation would give them.
    /// </summary>
    private async Task<SettlementResult> PlaceForPaidBookingAsync(
        BookingRecord booking, Guid memberKey, DateTime nowUtc)
    {
        switch (booking.Status)
        {
            case BookingStatus.Pending:
                await repository.ConfirmBookingAsync(booking.Id, nowUtc);
                return SettlementResult.Confirmed;

            case BookingStatus.Confirmed:
                return SettlementResult.Confirmed;

            case BookingStatus.Expired:
                TrainingClass? trainingClass = classes.Find(booking.ClassKey);

                if (trainingClass is not null
                    && trainingClass.StartUtc > nowUtc
                    && await repository.TryReconfirmBookingAsync(booking.Id, trainingClass.Capacity, nowUtc))
                {
                    logger.LogInformation(
                        "Booking {BookingId} was paid after its hold lapsed; the place was still free.",
                        booking.Id);
                    return SettlementResult.Reconfirmed;
                }

                await repository.IssueCreditAsync(memberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after its hold lapsed and the class had filled; "
                    + "a credit was issued instead.", booking.Id);
                return SettlementResult.Credited;

            default:
                // Cancelled while pending: an editor withdrew the class. CancelAllForClassAsync
                // credits only confirmed bookings, so this one got nothing - until now, when it
                // turns out to have been paid for.
                await repository.IssueCreditAsync(memberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after being cancelled; a credit was issued.", booking.Id);
                return SettlementResult.Credited;
        }
    }
```

Replace `AbandonPaymentAsync` with:

```csharp
    /// <summary>
    /// Abandons a payment and releases the place it was holding. Returns false when the payment
    /// had already left Pending, in which case nothing changed.
    /// </summary>
    public async Task<bool> AbandonPaymentAsync(PaymentRecord payment, string status, string? errorCode = null)
    {
        DateTime nowUtc = DateTime.UtcNow;

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, status, nowUtc, bankReference: null, errorCode);

        if (won is false)
        {
            logger.LogInformation(
                "Payment {Reference} was already settled; not abandoning it.", payment.Reference);
            return false;
        }

        if (payment.BookingId is { } bookingId)
        {
            await repository.ExpireBookingAsync(bookingId, nowUtc);
        }

        logger.LogInformation(
            "Payment {Reference} abandoned with status {Status}{Code}.",
            payment.Reference, status, errorCode is null ? string.Empty : $" ({errorCode})");
        return true;
    }
```

- [ ] **Step 5: Adjust the simulate actions**

In `SwishPaymentSurfaceController.cs`, `SimulatePaid` and `SimulateCancelled` compile as they are: `SettlePaymentAsync(payment)` and `AbandonPaymentAsync(payment, PaymentStatus.Cancelled)` still match. Only the `OwnedPendingPaymentAsync` status check is now redundant with the conditional write, which is fine; leave it.

- [ ] **Step 6: Type-check and run tests**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"` then `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: success, all pass.

- [ ] **Step 7: Commit**

```bash
git add Booking/Data/IBookingRepository.cs Booking/Data/BookingRepository.cs Booking/Services/BookingService.cs
git commit -m "Settle a payment with one conditional write and handle money that arrives late

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: The reservation defaults to 7 minutes

**Files:**
- Modify: `Booking/Services/MembershipSettings.cs:33-36`
- Modify: `ContentModel/NdstkContentModelInstaller.cs:92`
- Modify: `README.md:84`

- [ ] **Step 1: Change the default and its comment**

In `MembershipSettings.cs`, replace the `PaymentHoldMinutes` line and its comment:

```csharp
        // Long enough to outlive Swish's own request timeout - five and a half minutes from the
        // moment the payment starts, plus the seconds a callback takes to arrive - so the normal
        // course of events never has a paid booking whose hold has already been swept. The clock
        // is restarted when the payment starts, so however long the member looks at the page
        // first does not eat into it. Not longer: the hold blocks a real member from booking.
        PaymentHoldMinutes: 7,
```

- [ ] **Step 2: Update the field description in the installer**

Line 92 of `NdstkContentModelInstaller.cs` becomes:

```csharp
            factory.Property(BuiltInDataTypes.Numeric, "paymentHoldMinutes", "Betalningsreservation (minuter)", "Hur länge en obetald bokning håller sin plats, räknat från att Swish-betalningen startas. Måste vara längre än Swish egen tidsgräns på 5,5 minuter. Standard: 7.", 5),
```

(`EnsureGroupAsync` adds missing fields only, so an existing site keeps its stored description. That is acceptable; the value that matters is the default in code.)

- [ ] **Step 3: Update the README table**

Line 84: `| Betalningsreservation (minuter) | 7 |`

- [ ] **Step 4: Type-check and test**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"` then `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: success, all pass.

- [ ] **Step 5: Commit**

```bash
git add Booking/Services/MembershipSettings.cs ContentModel/NdstkContentModelInstaller.cs README.md
git commit -m "Hold a place for seven minutes so the hold outlives Swish's timeout

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Phase 2 — Swish provider and page

### Task 9: `SwishOptions` and `SwishPaymentProvider`

**Files:**
- Create: `Booking/Payments/Swish/SwishOptions.cs`
- Create: `Booking/Payments/Swish/SwishApiModels.cs`
- Create: `Booking/Payments/Swish/SwishPaymentProvider.cs`

**Interfaces:**
- Consumes: `IPaymentProvider` and models (Task 6), `SwishRequest` (Task 2), `SwishOutcome` (Task 3), `PaymentRecord` (Task 5).
- Produces: `SwishOptions` with `Enabled`, `PayeeAlias`, `ApiBaseUrl`, `QrApiBaseUrl`, `CertificatePath`, `CertificatePassword`, `CertificateThumbprint`, `SimulateErrorCode`; `SwishOptions.SectionName = "NDSTK:Swish"`; `SwishHttpClientNames.Api = "swish"`, `SwishHttpClientNames.Qr = "swish-qr"`; `SwishPaymentProvider : IPaymentProvider` with `Name == "Swish"`.

- [ ] **Step 1: Create `SwishOptions.cs`**

```csharp
namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Bound from <c>NDSTK:Swish</c>. Everything a real Swish payment needs that is not on the payment
/// row. <see cref="Enabled"/> plus a loadable certificate is what switches the mock off.
/// </summary>
public sealed class SwishOptions
{
    public const string SectionName = "NDSTK:Swish";

    public bool Enabled { get; set; }

    /// <summary>The club's Swish number, ten digits. Never shown to members.</summary>
    public string PayeeAlias { get; set; } = string.Empty;

    /// <summary>Production by default; appsettings.Development.json points at the simulator.</summary>
    public string ApiBaseUrl { get; set; } = "https://cpc.getswish.net/swish-cpcapi/";

    public string QrApiBaseUrl { get; set; } = "https://mpc.getswish.net/qrg-swish/";

    /// <summary>PKCS#12 with the private key and the chain, outside the web root.</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>A secret: appsettings.Secrets.json or an environment variable, never appsettings.json.</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>Alternative to the file: a certificate installed in LocalMachine\My.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>
    /// Development only. When set, replaces the message on every request so the simulator
    /// produces that outcome (RF07, TM01, …). Ignored outside the Development environment.
    /// </summary>
    public string SimulateErrorCode { get; set; } = string.Empty;

    public bool HasCertificateSource
        => !string.IsNullOrWhiteSpace(CertificatePath) || !string.IsNullOrWhiteSpace(CertificateThumbprint);
}

/// <summary>Names of the two HttpClients. The API client carries the certificate; the QR client does not.</summary>
public static class SwishHttpClientNames
{
    public const string Api = "swish";
    public const string Qr = "swish-qr";
}
```

- [ ] **Step 2: Create `SwishApiModels.cs`**

```csharp
using System.Text.Json.Serialization;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>The body of PUT /api/v2/paymentrequests/{instructionUUID}. Property names are Swish's.</summary>
internal sealed record SwishCreateRequest(
    [property: JsonPropertyName("payeePaymentReference")] string PayeePaymentReference,
    [property: JsonPropertyName("callbackUrl")] string CallbackUrl,
    [property: JsonPropertyName("payeeAlias")] string PayeeAlias,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("callbackIdentifier")] string CallbackIdentifier);

/// <summary>The payment request object Swish returns from GET and PATCH, and posts to the callback.</summary>
internal sealed record SwishPaymentRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("paymentReference")] string? PaymentReference,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("datePaid")] DateTime? DatePaid);

/// <summary>One element of the array a 422 carries.</summary>
internal sealed record SwishError(
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage);

/// <summary>The JSON Patch operation that cancels a request. The only one Swish accepts.</summary>
internal sealed record SwishCancelOperation(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("value")] string Value)
{
    public static readonly SwishCancelOperation[] Body = [new("replace", "/status", "cancelled")];
}
```

- [ ] **Step 3: Create `SwishPaymentProvider.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Swish Commerce over the v2 API: create with PUT, retrieve with GET, cancel with PATCH.
/// </summary>
/// <remarks>
/// The client named <see cref="SwishHttpClientNames.Api"/> carries the merchant certificate; see
/// SwishHttpClients. Nothing here logs the token or the callback identifier. The instruction id
/// is logged, because it is what support matches against Swish's own logs.
/// </remarks>
public sealed class SwishPaymentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SwishOptions> options,
    IHostEnvironment environment,
    ILogger<SwishPaymentProvider> logger) : IPaymentProvider
{
    public const string ProviderName = "Swish";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public async Task<PaymentStart> StartAsync(PaymentRecord payment, PaymentStartContext context)
    {
        SwishOptions swish = options.Value;
        var instructionId = SwishRequest.InstructionId(payment.Reference);
        var callbackIdentifier = SwishRequest.CallbackIdentifier();

        // Against the simulator, an error code in the message is how an outcome is chosen.
        // Read only in Development so no production setting can ever change what members see.
        var message = environment.IsDevelopment() && !string.IsNullOrWhiteSpace(swish.SimulateErrorCode)
            ? swish.SimulateErrorCode.Trim()
            : context.Message;

        var body = new SwishCreateRequest(
            SwishRequest.PaymentReference(payment.Reference),
            context.CallbackUrl,
            swish.PayeeAlias,
            SwishRequest.Amount(payment.AmountOre),
            "SEK",
            message,
            callbackIdentifier);

        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        HttpResponseMessage response;
        try
        {
            response = await client.PutAsJsonAsync($"api/v2/paymentrequests/{instructionId}", body, Json);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Swish could not be reached to create request {InstructionId}.", instructionId);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw await RefusalAsync(response, $"create request {instructionId}");
            }

            var token = response.Headers.TryGetValues("PaymentRequestToken", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrEmpty(token))
            {
                // Swish returns the token only for m-commerce, which is the only kind we send.
                // Its absence means the request is not what we think it is.
                logger.LogError("Swish created request {InstructionId} without a PaymentRequestToken.", instructionId);
                throw new PaymentProviderException("Swish returned no payment request token.");
            }

            logger.LogInformation("Swish request {InstructionId} created.", instructionId);
            return new PaymentStart(instructionId, token, callbackIdentifier);
        }
    }

    public async Task<PaymentOutcome> RetrieveAsync(string providerReference)
    {
        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"api/v1/paymentrequests/{providerReference}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Swish could not be reached to retrieve request {InstructionId}.", providerReference);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode is false)
            {
                throw await RefusalAsync(response, $"retrieve request {providerReference}");
            }

            SwishPaymentRequest? request = await response.Content.ReadFromJsonAsync<SwishPaymentRequest>(Json);
            return ToOutcome(request, providerReference);
        }
    }

    public async Task<PaymentOutcome> CancelAsync(string providerReference)
    {
        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Api);

        using var content = JsonContent.Create(SwishCancelOperation.Body, options: Json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");

        HttpResponseMessage response;
        try
        {
            response = await client.PatchAsync($"api/v1/paymentrequests/{providerReference}", content);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Swish could not be reached to cancel request {InstructionId}.", providerReference);
            throw new PaymentProviderException("Swish could not be reached.", inner: exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // RP07: already final. Not a failure - the answer is whatever it became.
                logger.LogInformation(
                    "Swish request {InstructionId} could not be cancelled; it is already final.", providerReference);
                return await RetrieveAsync(providerReference);
            }

            if (response.IsSuccessStatusCode is false)
            {
                throw await RefusalAsync(response, $"cancel request {providerReference}");
            }

            SwishPaymentRequest? request = await response.Content.ReadFromJsonAsync<SwishPaymentRequest>(Json);
            logger.LogInformation("Swish request {InstructionId} cancelled.", providerReference);
            return ToOutcome(request, providerReference);
        }
    }

    private static PaymentOutcome ToOutcome(SwishPaymentRequest? request, string providerReference)
    {
        if (request?.Status is null)
        {
            throw new PaymentProviderException($"Swish returned no status for request {providerReference}.");
        }

        ProviderStatus status = request.Status.ToUpperInvariant() switch
        {
            SwishOutcome.Paid => ProviderStatus.Paid,
            SwishOutcome.Declined => ProviderStatus.Declined,
            SwishOutcome.Error => ProviderStatus.Error,
            SwishOutcome.Cancelled => ProviderStatus.Cancelled,
            _ => ProviderStatus.Created,
        };

        return new PaymentOutcome(status, request.PaymentReference, request.ErrorCode, request.DatePaid);
    }

    /// <summary>
    /// Turns a non-success response into an exception that carries Swish's error code when the
    /// body is the 422 error array, and the HTTP status otherwise.
    /// </summary>
    private async Task<PaymentProviderException> RefusalAsync(HttpResponseMessage response, string what)
    {
        string? code = null;
        string? detail = null;

        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden)
        {
            try
            {
                SwishError[]? errors = await response.Content.ReadFromJsonAsync<SwishError[]>(Json);
                SwishError? first = errors?.FirstOrDefault();
                code = first?.ErrorCode;
                detail = first?.ErrorMessage;
            }
            catch (JsonException)
            {
                // A body that is not the documented array. The status code is still informative.
            }
        }

        logger.LogError(
            "Swish refused to {What}: HTTP {Status}{Code}{Detail}.",
            what, (int)response.StatusCode,
            code is null ? string.Empty : $" {code}",
            detail is null ? string.Empty : $" ({detail})");

        return new PaymentProviderException(
            $"Swish refused to {what} with HTTP {(int)response.StatusCode}.", code);
    }
}
```

- [ ] **Step 4: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success. Nothing registers the provider yet.

- [ ] **Step 5: Commit**

```bash
git add Booking/Payments/Swish/SwishOptions.cs Booking/Payments/Swish/SwishApiModels.cs Booking/Payments/Swish/SwishPaymentProvider.cs
git commit -m "Speak the Swish Commerce v2 API: create, retrieve, cancel

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Certificate loading, HTTP clients, provider selection, configuration

**Files:**
- Create: `Booking/Payments/Swish/SwishCertificateLoader.cs`
- Create: `Booking/Payments/Swish/SwishHttpClients.cs`
- Create: `Booking/Payments/PaymentProviderFactory.cs`
- Create: `Booking/Payments/PaymentProviderAnnouncer.cs`
- Modify: `Booking/BookingComposer.cs:62-64`
- Modify: `appsettings.json` (after the `Esatto` section), `appsettings.Development.json` (inside `NDSTK`)

**Interfaces:**
- Consumes: `SwishOptions`, `SwishPaymentProvider` (Task 9), `SwishMockPaymentProvider` (Task 6).
- Produces: `X509Certificate2? SwishCertificateLoader.Load()` (singleton, caches the result); `IUmbracoBuilder AddSwishPayments(this IUmbracoBuilder builder)`.

- [ ] **Step 1: Create `SwishCertificateLoader.cs`**

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Loads the merchant certificate once. Null, with an error in the log, when it cannot be loaded -
/// the factory then falls back to the mock, and says so.
/// </summary>
/// <remarks>
/// Not EphemeralKeySet. SChannel cannot present an ephemeral private key as a TLS client
/// certificate, and both development and production are Windows. MachineKeySet puts the key in
/// the machine container, which the IIS application pool identity can read.
/// </remarks>
public sealed class SwishCertificateLoader(
    IOptions<SwishOptions> options,
    ILogger<SwishCertificateLoader> logger)
{
    private readonly Lazy<X509Certificate2?> certificate = new(() => Load(options.Value, logger));

    public X509Certificate2? Load() => certificate.Value;

    private static X509Certificate2? Load(SwishOptions swish, ILogger logger)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(swish.CertificateThumbprint))
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);

                X509Certificate2? found = store.Certificates
                    .Find(X509FindType.FindByThumbprint, swish.CertificateThumbprint.Trim(), validOnly: false)
                    .FirstOrDefault();

                if (found is null)
                {
                    logger.LogError("No certificate with the configured thumbprint is in LocalMachine\\My.");
                    return null;
                }

                if (found.HasPrivateKey is false)
                {
                    logger.LogError("The Swish certificate in the store has no private key.");
                    return null;
                }

                return found;
            }

            if (!string.IsNullOrWhiteSpace(swish.CertificatePath))
            {
                X509Certificate2 loaded = X509CertificateLoader.LoadPkcs12FromFile(
                    swish.CertificatePath,
                    swish.CertificatePassword,
                    X509KeyStorageFlags.MachineKeySet);

                if (loaded.HasPrivateKey is false)
                {
                    logger.LogError("The Swish certificate file has no private key.");
                    return null;
                }

                return loaded;
            }

            return null;
        }
        catch (Exception exception)
        {
            // The path and the reason are logged; the password never is.
            logger.LogError(exception, "The Swish certificate could not be loaded from {Path}.", swish.CertificatePath);
            return null;
        }
    }
}
```

- [ ] **Step 2: Create `SwishHttpClients.cs`**

```csharp
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>Registers the two named clients and the certificate loader.</summary>
public static class SwishHttpClients
{
    public static IUmbracoBuilder AddSwishHttpClients(this IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<SwishCertificateLoader>();

        builder.Services.AddHttpClient(SwishHttpClientNames.Api, (services, client) =>
            {
                client.BaseAddress = new Uri(services.GetRequiredService<IOptions<SwishOptions>>().Value.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var handler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                };

                X509Certificate2? certificate = services.GetRequiredService<SwishCertificateLoader>().Load();
                if (certificate is not null)
                {
                    handler.ClientCertificates.Add(certificate);
                }

                return handler;
            });

        // The QR generator is public: no certificate, shorter timeout, an image in reply.
        builder.Services.AddHttpClient(SwishHttpClientNames.Qr, (services, client) =>
        {
            client.BaseAddress = new Uri(services.GetRequiredService<IOptions<SwishOptions>>().Value.QrApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return builder;
    }
}
```

- [ ] **Step 3: Create `PaymentProviderFactory.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NDSTK.Booking.Payments.Swish;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Decides which provider takes money: Swish when it is enabled and the certificate loads,
/// the mock otherwise. Resolved once, as a singleton.
/// </summary>
public static class PaymentProviderFactory
{
    public static IPaymentProvider Create(IServiceProvider services)
    {
        SwishOptions options = services.GetRequiredService<IOptions<SwishOptions>>().Value;
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PaymentProviderFactory));

        if (options.Enabled is false)
        {
            logger.LogWarning("Payment provider: SwishMock. NDSTK:Swish:Enabled is false; no money is taken.");
            return new SwishMockPaymentProvider();
        }

        if (options.HasCertificateSource is false)
        {
            logger.LogWarning("Payment provider: SwishMock. Swish is enabled but no certificate is configured.");
            return new SwishMockPaymentProvider();
        }

        if (string.IsNullOrWhiteSpace(options.PayeeAlias))
        {
            logger.LogWarning("Payment provider: SwishMock. Swish is enabled but NDSTK:Swish:PayeeAlias is empty.");
            return new SwishMockPaymentProvider();
        }

        if (services.GetRequiredService<SwishCertificateLoader>().Load() is null)
        {
            // The loader has already logged why.
            logger.LogWarning("Payment provider: SwishMock. The Swish certificate did not load.");
            return new SwishMockPaymentProvider();
        }

        logger.LogInformation("Payment provider: Swish, against {ApiBaseUrl}.", options.ApiBaseUrl);
        return ActivatorUtilities.CreateInstance<SwishPaymentProvider>(services);
    }
}
```

- [ ] **Step 4: Create `PaymentProviderAnnouncer.cs`**

```csharp
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace NDSTK.Booking.Payments;

/// <summary>
/// Resolves the provider at startup so the factory's "Payment provider: …" line is on the first
/// page of the log, not buried after the first booking of the day.
/// </summary>
public sealed class PaymentProviderAnnouncer(IPaymentProvider provider)
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        // Resolving the constructor parameter did the work. Touching Name keeps the analyser quiet
        // about an unused parameter without adding a second log line.
        _ = provider.Name;
    }
}
```

- [ ] **Step 5: Wire it in `BookingComposer`**

Add `using NDSTK.Booking.Payments.Swish;` to the usings. Replace lines 62-64 (the comment and the mock registration) with:

```csharp
        // Swish when enabled and the certificate loads, the mock otherwise. The factory logs which,
        // and the announcer makes it log at startup.
        builder.Services.AddOptions<SwishOptions>().Bind(builder.Config.GetSection(SwishOptions.SectionName));
        builder.AddSwishHttpClients();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IPaymentProvider>(PaymentProviderFactory.Create);
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, PaymentProviderAnnouncer>();
```

- [ ] **Step 6: Configuration**

In `appsettings.json`, add after the `"Esatto": {…}` block (before `"ConnectionStrings"`):

```json
  "NDSTK": {
    // Off until the club has its Swish Handel agreement and certificate. With Enabled false the
    // mocked payment page stays, marked Demoläge, and the boot log says "Payment provider:
    // SwishMock". PayeeAlias, CertificatePath and CertificatePassword belong in
    // appsettings.Secrets.json or environment variables (NDSTK__Swish__CertificatePassword),
    // never here.
    "Swish": {
      "Enabled": false,
      "ApiBaseUrl": "https://cpc.getswish.net/swish-cpcapi/",
      "QrApiBaseUrl": "https://mpc.getswish.net/qrg-swish/"
    }
  },
```

In `appsettings.Development.json`, inside the existing `"NDSTK"` object, add after `CookieScanApiUser`:

```json
    // The Merchant Swish Simulator. Enabled, PayeeAlias and the certificate still come from
    // appsettings.Secrets.json, so without those the mock remains active locally too.
    "Swish": {
      "ApiBaseUrl": "https://mss.cpc.getswish.net/swish-cpcapi/"
    }
```

- [ ] **Step 7: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success. If `AddNotificationHandler` needs `using Umbraco.Cms.Core.Notifications;`, it is already imported in the composer.

- [ ] **Step 8: Commit**

```bash
git add Booking/Payments/Swish/SwishCertificateLoader.cs Booking/Payments/Swish/SwishHttpClients.cs Booking/Payments/PaymentProviderFactory.cs Booking/Payments/PaymentProviderAnnouncer.cs Booking/BookingComposer.cs appsettings.json appsettings.Development.json
git commit -m "Choose Swish or the mock from configuration and the certificate

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint for Carl (next relaunch):** the first lines of the log include `Payment provider: SwishMock. NDSTK:Swish:Enabled is false; no money is taken.` and the payment page behaves exactly as before.

---

### Task 11: `StartPaymentAsync`, `ReconcileAsync`, `CancelPaymentAsync`

**Files:**
- Modify: `Booking/Services/BookingService.cs`

**Interfaces:**
- Consumes: repository methods (Task 7), `IPaymentProvider` (Task 6), `SwishRequest.Message` (Task 2).
- Produces:

```csharp
public enum StartPaymentResult { Started, AlreadyStarted, NotPending, ProviderUnavailable }
public enum CancelPaymentResult { Cancelled, AlreadyFinal, ProviderUnavailable }
Task<StartPaymentResult> StartPaymentAsync(PaymentRecord payment, string callbackUrl);
Task<PaymentRecord> ReconcileAsync(PaymentRecord payment, DateTime nowUtc);   // may throw PaymentProviderException
Task<CancelPaymentResult> CancelPaymentAsync(PaymentRecord payment);
```

- [ ] **Step 1: Add the enums after `SettlementResult`**

```csharp
/// <summary>What pressing "Betala med Swish" did.</summary>
public enum StartPaymentResult
{
    Started,

    /// <summary>A request already exists: a second tab, or a refresh. The page shows it.</summary>
    AlreadyStarted,

    /// <summary>The payment is no longer pending. The page shows the outcome.</summary>
    NotPending,

    /// <summary>Swish refused or could not be reached. Nothing changed; the member can retry.</summary>
    ProviderUnavailable,
}

/// <summary>What pressing "Avbryt" did.</summary>
public enum CancelPaymentResult
{
    Cancelled,

    /// <summary>Swish had already decided - typically PAID, a second after the press. Applied.</summary>
    AlreadyFinal,

    /// <summary>Swish could not be reached, so nothing was cancelled anywhere. The hold stands.</summary>
    ProviderUnavailable,
}
```

- [ ] **Step 2: Add the three methods to `BookingService`, after `AbandonPaymentAsync`**

```csharp
    /// <summary>
    /// Creates the request at the provider and records it. Restarts the hold, so the reservation
    /// outlives Swish's own timeout however long the member looked at the page first.
    /// </summary>
    public async Task<StartPaymentResult> StartPaymentAsync(PaymentRecord payment, string callbackUrl)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            return StartPaymentResult.NotPending;
        }

        if (payment.ProviderReference is not null)
        {
            return StartPaymentResult.AlreadyStarted;
        }

        DateTime nowUtc = DateTime.UtcNow;
        var context = new PaymentStartContext(callbackUrl, await MessageForAsync(payment));

        PaymentStart start;
        try
        {
            start = await paymentProvider.StartAsync(payment, context);
        }
        catch (PaymentProviderException exception)
        {
            logger.LogWarning(
                "Payment {Reference} could not be started at the provider{Code}.",
                payment.Reference, exception.ErrorCode is null ? string.Empty : $" ({exception.ErrorCode})");
            return StartPaymentResult.ProviderUnavailable;
        }

        var recorded = await repository.TryStartPaymentAsync(
            payment.Id, start.ProviderReference, start.Token, start.CallbackIdentifier, nowUtc);

        if (recorded is false)
        {
            // Two tabs pressed Betala at once and the other one won. Swish now holds two requests
            // for one payment; withdraw ours so the member's app shows one.
            try
            {
                await paymentProvider.CancelAsync(start.ProviderReference);
            }
            catch (PaymentProviderException)
            {
                logger.LogWarning(
                    "Duplicate request {InstructionId} for payment {Reference} could not be withdrawn.",
                    start.ProviderReference, payment.Reference);
            }

            return StartPaymentResult.AlreadyStarted;
        }

        if (payment.BookingId is { } bookingId)
        {
            await repository.TryRestartHoldAsync(
                bookingId, nowUtc.AddMinutes(settings.Get().PaymentHoldMinutes));
        }

        logger.LogInformation("Payment {Reference} started as {InstructionId}.", payment.Reference, start.ProviderReference);
        return StartPaymentResult.Started;
    }

    /// <summary>
    /// Asks the provider where the payment stands and applies the answer. The one routine behind
    /// the page's poll, Swish's callback and the reminder job. Throws
    /// <see cref="PaymentProviderException"/> when the provider cannot be reached; callers decide
    /// whether that is worth more than a log line.
    /// </summary>
    public async Task<PaymentRecord> ReconcileAsync(PaymentRecord payment, DateTime nowUtc)
    {
        if (payment.Status != PaymentStatus.Pending || payment.ProviderReference is null)
        {
            return payment;
        }

        await repository.StampPaymentCheckedAsync(payment.Id, nowUtc);

        PaymentOutcome outcome = await paymentProvider.RetrieveAsync(payment.ProviderReference);
        await ApplyOutcomeAsync(payment, outcome);

        return await repository.GetPaymentByReferenceAsync(payment.Reference) ?? payment;
    }

    /// <summary>
    /// Withdraws the request at the provider, then abandons the payment and releases the place.
    /// </summary>
    /// <remarks>
    /// If the provider cannot be reached, nothing is cancelled locally either. Cancelling here
    /// while the request stays open at Swish would let the member pay in the app for a payment
    /// this site no longer expects; the hold simply runs out instead.
    /// </remarks>
    public async Task<CancelPaymentResult> CancelPaymentAsync(PaymentRecord payment)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            return CancelPaymentResult.AlreadyFinal;
        }

        if (payment.ProviderReference is not null)
        {
            PaymentOutcome outcome;
            try
            {
                outcome = await paymentProvider.CancelAsync(payment.ProviderReference);
            }
            catch (PaymentProviderException)
            {
                logger.LogWarning(
                    "Payment {Reference} could not be cancelled at the provider; leaving it pending.",
                    payment.Reference);
                return CancelPaymentResult.ProviderUnavailable;
            }

            if (outcome.Status != ProviderStatus.Cancelled)
            {
                // Swish had already decided. Whatever it decided is what happened.
                await ApplyOutcomeAsync(payment, outcome);
                return CancelPaymentResult.AlreadyFinal;
            }
        }

        await AbandonPaymentAsync(payment, PaymentStatus.Cancelled);
        return CancelPaymentResult.Cancelled;
    }

    private async Task ApplyOutcomeAsync(PaymentRecord payment, PaymentOutcome outcome)
    {
        switch (outcome.Status)
        {
            case ProviderStatus.Paid:
                await SettlePaymentAsync(payment, outcome.BankReference);
                break;

            case ProviderStatus.Declined:
            case ProviderStatus.Cancelled:
                await AbandonPaymentAsync(payment, PaymentStatus.Cancelled);
                break;

            case ProviderStatus.Error:
                await AbandonPaymentAsync(payment, PaymentStatus.Failed, outcome.ErrorCode);
                break;

            case ProviderStatus.Created:
                break;
        }
    }

    /// <summary>The text in the member's Swish history, built from the class so it always validates.</summary>
    private async Task<string> MessageForAsync(PaymentRecord payment)
    {
        if (payment.BookingId is null)
        {
            return SwishRequest.Message(null, null);
        }

        BookingRecord? booking = await repository.GetBookingAsync(payment.BookingId.Value);
        TrainingClass? trainingClass = booking is null ? null : classes.Find(booking.ClassKey);

        return SwishRequest.Message(
            trainingClass?.Title ?? "Träning",
            booking is null ? null : SwedishTime.ToSwedish(booking.ClassStartUtc));
    }
```

- [ ] **Step 3: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success. `using NDSTK.Booking.Payments;` is already at the top of `BookingService.cs`.

- [ ] **Step 4: Commit**

```bash
git add Booking/Services/BookingService.cs
git commit -m "Start, reconcile and cancel a payment through the provider

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: Surface actions `Start`, `Status`, `Qr`, `Cancel`; rate limits; the shared payment page URL

**Files:**
- Create: `Booking/Web/PaymentPageUrl.cs`
- Create: `Booking/Payments/Swish/SwishCallbackUrl.cs`
- Create: `Booking/Payments/Swish/SwishQrService.cs`
- Modify: `Booking/Web/BookingRateLimits.cs`
- Modify: `Program.cs:45-54` (add two policies after `MemberActions`)
- Modify: `Booking/Web/SwishPaymentSurfaceController.cs`
- Modify: `Booking/Web/BookingSurfaceController.cs:126-136`, `Booking/Web/FamilyUpgradeSurfaceController.cs:147-157` (replace the private `PaymentPageUrl` methods)
- Modify: `Booking/BookingComposer.cs` (register `SwishCallbackUrl`, `SwishQrService`)

**Interfaces:**
- Consumes: `BookingService.StartPaymentAsync/ReconcileAsync/CancelPaymentAsync` (Task 11), `SwishOptions`, client names (Task 9).
- Produces:
  - `string? PaymentPageUrl.For(IPublishedContentQuery query, IPublishedUrlProvider urls, Guid reference)`
  - `string SwishCallbackUrl.Build()` — `{UmbracoApplicationUrl}api/swish/callback`
  - `Task<byte[]?> SwishQrService.GetSvgAsync(Guid paymentReference, string token)`
  - `BookingRateLimits.PaymentStatus = "ndstk-payment-status"`, `BookingRateLimits.Callback = "ndstk-swish-callback"`
  - Surface actions `Start(Guid reference)`, `Status(Guid reference)`, `Qr(Guid reference)`, `Cancel(Guid reference)`; `TempData["PaymentError"]` when Swish is unreachable.

- [ ] **Step 1: Create `PaymentPageUrl.cs`**

```csharp
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>
/// Where a member is sent to pay. Resolved from content rather than hard-coded so an editor can
/// rename or move the page, and in one place so the three controllers that send members there
/// cannot drift apart.
/// </summary>
public static class PaymentPageUrl
{
    public static string? For(IPublishedContentQuery contentQuery, IPublishedUrlProvider urlProvider, Guid reference)
    {
        IPublishedContent? page = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("swishPayment"))
            .FirstOrDefault();

        return page is null
            ? null
            : $"{page.Url(urlProvider)}?ref={Uri.EscapeDataString(reference.ToString())}";
    }
}
```

Then in `BookingSurfaceController.cs` and `FamilyUpgradeSurfaceController.cs`, delete each private `PaymentPageUrl(Guid reference)` method and replace its one call with `PaymentPageUrl.For(contentQuery, PublishedUrlProvider, …)`. In `BookingSurfaceController.Book` the call becomes `PaymentPageUrl.For(contentQuery, PublishedUrlProvider, attempt.PaymentReference!.Value)`; in `FamilyUpgradeSurfaceController.Upgrade` it becomes `PaymentPageUrl.For(contentQuery, PublishedUrlProvider, payment.Reference)`.

- [ ] **Step 2: Create `SwishCallbackUrl.cs`**

```csharp
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// The URL Swish posts the outcome to. Built from the application URL Umbraco already knows for
/// each environment, so no new setting can be wrong. Locally it names a host the simulator cannot
/// reach, and the page's poll settles the payment instead.
/// </summary>
public sealed class SwishCallbackUrl(IOptions<WebRoutingSettings> webRouting)
{
    public const string Path = "api/swish/callback";

    public string Build()
    {
        var applicationUrl = webRouting.Value.UmbracoApplicationUrl;
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            throw new InvalidOperationException(
                "Umbraco:CMS:WebRouting:UmbracoApplicationUrl must be set for the Swish callback URL.");
        }

        return new Uri(new Uri(applicationUrl.TrimEnd('/') + "/"), Path).ToString();
    }
}
```

- [ ] **Step 3: Create `SwishQrService.cs`**

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace NDSTK.Booking.Payments.Swish;

/// <summary>
/// Turns a payment request token into the QR image Swish's own generator draws for it. Cached per
/// payment for ten minutes, which outlives any request Swish will still honour, so a page that
/// polls and reloads does not fetch the same image again and again.
/// </summary>
public sealed class SwishQrService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<SwishQrService> logger)
{
    private sealed record QrRequest(string Token, string Format, int Size);

    public async Task<byte[]?> GetSvgAsync(Guid paymentReference, string token)
    {
        var key = $"swish-qr:{paymentReference:N}";
        if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
        {
            return cached;
        }

        HttpClient client = httpClientFactory.CreateClient(SwishHttpClientNames.Qr);

        try
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "api/v1/commerce", new QrRequest(token, "svg", 300));

            if (response.IsSuccessStatusCode is false)
            {
                logger.LogWarning(
                    "The Swish QR service answered HTTP {Status} for payment {Reference}.",
                    (int)response.StatusCode, paymentReference);
                return null;
            }

            var svg = await response.Content.ReadAsByteArrayAsync();
            cache.Set(key, svg, TimeSpan.FromMinutes(10));
            return svg;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "The Swish QR service could not be reached for payment {Reference}.", paymentReference);
            return null;
        }
    }
}
```

- [ ] **Step 4: Rate limit policies**

In `BookingRateLimits.cs`, add:

```csharp
    /// <summary>
    /// The payment page's status poll and QR image. Every three seconds from a member who may have
    /// two tabs open, so it gets its own budget rather than eating into <see cref="MemberActions"/>.
    /// </summary>
    public const string PaymentStatus = "ndstk-payment-status";

    /// <summary>
    /// Swish's callback. Sized for its retry schedule - ten attempts per payment - times however
    /// many members pay in the same minute, all from one address. Not a budget people reach.
    /// </summary>
    public const string Callback = "ndstk-swish-callback";
```

In `Program.cs`, after the `MemberActions` policy (line 54) add:

```csharp
    options.AddPolicy(BookingRateLimits.PaymentStatus, context =>
        RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            // A poll every three seconds from two tabs is forty a minute; a family sharing a
            // connection while two of them pay is twice that.
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.AddPolicy(BookingRateLimits.Callback, context =>
        RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            // Swish retries a failed callback up to ten times, and every payment made in the same
            // minute arrives from the same address.
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
```

- [ ] **Step 5: Rewrite `SwishPaymentSurfaceController`**

Replace the file's contents with:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Payments.Swish;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Filters;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>
/// The actions behind the payment page: start the Swish request, report where it stands, draw its
/// QR code, cancel it. Plus the two simulate buttons, which exist only while the mock is the
/// provider.
/// </summary>
/// <remarks>
/// Every action loads the payment through <see cref="OwnedPaymentAsync"/>, which verifies it
/// belongs to the signed-in member and answers "not found" otherwise - a reference must not be
/// probeable for existence.
/// </remarks>
public sealed class SwishPaymentSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IMemberManager memberManager,
    IPublishedContentQuery contentQuery,
    IBookingRepository repository,
    BookingService bookings,
    IPaymentProvider paymentProvider,
    SwishCallbackUrl callbackUrl,
    SwishQrService qr,
    ILogger<SwishPaymentSurfaceController> logger)
    : SurfaceController(
        umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    private const string ProviderUnavailableMessage =
        "Swish går inte att nå just nu. Försök igen om en stund.";

    /// <summary>The seconds a poll waits before asking Swish again for the same payment.</summary>
    private static readonly TimeSpan PollSpacing = TimeSpan.FromSeconds(5);

    private bool IsMock => paymentProvider.Name == SwishMockPaymentProvider.ProviderName;

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Start(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        StartPaymentResult result = await bookings.StartPaymentAsync(payment, callbackUrl.Build());

        if (result == StartPaymentResult.ProviderUnavailable)
        {
            TempData["PaymentError"] = ProviderUnavailableMessage;
        }

        return Redirect(PaymentPageUrl.For(contentQuery, PublishedUrlProvider, reference) ?? PortalUrl());
    }

    /// <summary>
    /// Where the payment stands, as JSON for the page's poll. Asks Swish when it is pending, has a
    /// request, and was not asked in the last few seconds.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(BookingRateLimits.PaymentStatus)]
    public async Task<IActionResult> Status(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        DateTime nowUtc = DateTime.UtcNow;

        var due = payment.Status == PaymentStatus.Pending
            && payment.ProviderReference is not null
            && (payment.LastCheckedUtc is null || payment.LastCheckedUtc <= nowUtc - PollSpacing);

        if (due)
        {
            try
            {
                payment = await bookings.ReconcileAsync(payment, nowUtc);
            }
            catch (PaymentProviderException exception)
            {
                // The poll will try again. The member sees "väntar", which is the truth.
                logger.LogWarning(exception, "Reconciling payment {Reference} from the page failed.", reference);
            }
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new { status = payment.Status, terminal = payment.Status != PaymentStatus.Pending });
    }

    [HttpGet]
    [EnableRateLimiting(BookingRateLimits.PaymentStatus)]
    public async Task<IActionResult> Qr(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment?.ProviderToken is null || IsMock)
        {
            return NotFound();
        }

        var svg = await qr.GetSvgAsync(payment.Reference, payment.ProviderToken);
        if (svg is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=600";
        return File(svg, "image/svg+xml");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> Cancel(Guid reference)
    {
        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null)
        {
            return NotFound();
        }

        switch (await bookings.CancelPaymentAsync(payment))
        {
            case CancelPaymentResult.Cancelled:
                TempData["BookingError"] = "Betalningen avbröts, så platsen är inte bokad.";
                return Redirect(PortalUrl());

            case CancelPaymentResult.ProviderUnavailable:
                TempData["PaymentError"] = ProviderUnavailableMessage;
                break;
        }

        // AlreadyFinal: the page shows what Swish decided.
        return Redirect(PaymentPageUrl.For(contentQuery, PublishedUrlProvider, reference) ?? PortalUrl());
    }

    // ------------------------------------------------------------ the mock's buttons

    /// <summary>Stands in for a PAID callback. Only while the mock is the provider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulatePaid(Guid reference)
    {
        if (IsMock is false)
        {
            return NotFound();
        }

        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return NotFound();
        }

        await bookings.SettlePaymentAsync(payment);

        TempData["BookingMessage"] = "Betalningen är genomförd och din träning är bokad.";
        return Redirect(PortalUrl());
    }

    /// <summary>Stands in for a DECLINED callback. Only while the mock is the provider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateUmbracoFormRouteString]
    [EnableRateLimiting(BookingRateLimits.MemberActions)]
    public async Task<IActionResult> SimulateCancelled(Guid reference)
    {
        if (IsMock is false)
        {
            return NotFound();
        }

        PaymentRecord? payment = await OwnedPaymentAsync(reference);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return NotFound();
        }

        await bookings.AbandonPaymentAsync(payment, PaymentStatus.Cancelled);

        TempData["BookingError"] = "Betalningen avbröts, så platsen är inte bokad.";
        return Redirect(PortalUrl());
    }

    /// <summary>
    /// The payment, only if it belongs to the signed-in member. "Not found" for anything else, so
    /// a reference cannot be probed for existence.
    /// </summary>
    private async Task<PaymentRecord?> OwnedPaymentAsync(Guid reference)
    {
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            return null;
        }

        PaymentRecord? payment = await repository.GetPaymentByReferenceAsync(reference);

        if (payment is null || payment.MemberKey != user.Key)
        {
            logger.LogWarning("A payment action was attempted by someone who does not own it.");
            return null;
        }

        return payment;
    }

    private string PortalUrl()
    {
        IPublishedContent? portal = contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType("memberPortal"))
            .FirstOrDefault();

        return portal?.Url(PublishedUrlProvider) ?? "/";
    }
}
```

- [ ] **Step 6: Register the two new services in `BookingComposer`**

After the `PaymentProviderAnnouncer` line from Task 10:

```csharp
        builder.Services.AddSingleton<SwishCallbackUrl>();
        builder.Services.AddSingleton<SwishQrService>();
```

- [ ] **Step 7: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success.

- [ ] **Step 8: Commit**

```bash
git add Booking/Web/PaymentPageUrl.cs Booking/Payments/Swish/SwishCallbackUrl.cs Booking/Payments/Swish/SwishQrService.cs Booking/Web/BookingRateLimits.cs Program.cs Booking/Web/SwishPaymentSurfaceController.cs Booking/Web/BookingSurfaceController.cs Booking/Web/FamilyUpgradeSurfaceController.cs Booking/BookingComposer.cs
git commit -m "Add the start, status, QR and cancel actions behind the payment page

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: The four-state payment page

**Files:**
- Modify: `Booking/Web/SwishPaymentController.cs`
- Modify: `Views/SwishPayment.cshtml`
- Create: `wwwroot/static/js/swish-payment.js`
- Modify: `wwwroot/static/css/site.css` (after `.swish__note`, line ~966)

**Interfaces:**
- Consumes: surface actions (Task 12), `SwishRequest.AppLink` (Task 2), `SwishOutcome.Resolve` (Task 3), `SwishMockPaymentProvider.ProviderName`.
- Produces: `SwishPaymentViewModel` gains `IsMock`, `IsStarted`, `AppLink`, `QrUrl`, `StatusUrl`, `IsFailed`, `IsCancelled`, `OutcomeMessage`, `CreditIssued`.

- [ ] **Step 1: Extend the view model and controller**

Replace `SwishPaymentViewModel` in `SwishPaymentController.cs` with:

```csharp
/// <summary>What the payment page should render.</summary>
public sealed record SwishPaymentViewModel(
    Guid Reference,
    int AmountOre,
    int MembershipFeeOre,
    int FamilyFeeOre,
    int ClassFeeOre,
    /// <summary>Null for a purchase with no class attached, which today means a family upgrade.</summary>
    int? BookingId,
    string? ClassTitle,
    DateTime? ClassStartUtc,
    string Status,
    string? BookingStatus,
    DateTime? HoldExpiresUtc,
    /// <summary>The mock is active: Demoläge and the simulate buttons.</summary>
    bool IsMock,
    /// <summary>A request exists at the provider: show the app link and QR, and poll.</summary>
    bool IsStarted,
    string? AppLink,
    string? QrUrl,
    string? StatusUrl,
    /// <summary>The sentence for a failed or cancelled payment, from SwishOutcome.</summary>
    string? OutcomeMessage)
{
    public bool IsPending => Status == PaymentStatus.Pending;
    public bool IsPaid => Status == PaymentStatus.Paid;
    public bool IsFailed => Status == PaymentStatus.Failed;
    public bool IsCancelled => Status == PaymentStatus.Cancelled;

    /// <summary>
    /// Paid, but the place was gone by the time the money arrived, so the member holds a credit
    /// instead. True whenever a paid payment's booking is not confirmed - the only way that
    /// happens is the late-payment rule in SettlePaymentAsync.
    /// </summary>
    public bool CreditIssued
        => IsPaid && BookingId is not null && BookingStatus != Domain.BookingStatus.Confirmed;

    /// <summary>
    /// Minutes left on the reservation, rounded up, never below zero.
    /// </summary>
    /// <remarks>
    /// Rounded up rather than truncated. A page rendered a fraction of a second after a hold is
    /// created has a few milliseconds less than the full duration left - which truncates to one
    /// minute fewer and reads as though a minute was lost before the member did anything. Rounding
    /// up shows the whole duration for the first minute, which is what "reserverad i N minuter
    /// till" means. The countdown script uses the same rule, so the two never disagree.
    /// </remarks>
    public int MinutesLeft => HoldExpiresUtc is { } expires
        ? Math.Max(0, (int)Math.Ceiling((expires - DateTime.UtcNow).TotalMinutes))
        : 0;

    /// <summary>
    /// The expiry as an ISO 8601 instant for the countdown script.
    /// </summary>
    /// <remarks>
    /// The kind is forced to UTC before formatting. NPoco hands the value back as
    /// <see cref="DateTimeKind.Unspecified"/>, and "o" omits the trailing Z for that kind - which
    /// JavaScript's Date.parse then reads as *local* time, putting the countdown one or two hours
    /// out depending on the season.
    /// </remarks>
    public string? HoldExpiresIso => HoldExpiresUtc is { } expires
        ? DateTime.SpecifyKind(expires, DateTimeKind.Utc).ToString("o")
        : null;
}
```

Change the controller's constructor to also take `IPaymentProvider paymentProvider` and `IPublishedContentQuery contentQuery`, add `using NDSTK.Booking.Payments;` and `using Umbraco.Cms.Core;` and `using Umbraco.Cms.Core.Routing;`, and replace `LoadAsync` with:

```csharp
    private async Task<SwishPaymentViewModel?> LoadAsync(Guid? reference)
    {
        if (reference is null)
        {
            return null;
        }

        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            return null;
        }

        PaymentRecord? payment = await repository.GetPaymentByReferenceAsync(reference.Value);

        // The ownership check. Without it, guessing or sharing a reference would let anyone view -
        // and, through the actions below, settle - somebody else's payment. Treated as "not found"
        // rather than "forbidden" so a reference cannot be probed for existence.
        if (payment is null || payment.MemberKey != user.Key)
        {
            logger.LogWarning("A payment reference was requested by someone who does not own it.");
            return null;
        }

        BookingRecord? booking = payment.BookingId is { } bookingId
            ? await repository.GetBookingAsync(bookingId)
            : null;

        TrainingClass? trainingClass = booking is null ? null : classes.Find(booking.ClassKey);

        var isMock = paymentProvider.Name == SwishMockPaymentProvider.ProviderName;
        var isStarted = payment.ProviderReference is not null;

        // The page itself is where the Swish app sends the member back to, so it is the return URL.
        var pageUrl = PaymentPageUrl.For(contentQuery, publishedUrlProvider, payment.Reference);
        var absolutePageUrl = pageUrl is null
            ? null
            : new Uri(new Uri($"{Request.Scheme}://{Request.Host}"), pageUrl).ToString();

        var appLink = isStarted && !isMock && payment.ProviderToken is not null && absolutePageUrl is not null
            ? SwishRequest.AppLink(payment.ProviderToken, absolutePageUrl)
            : null;

        var query = $"?reference={Uri.EscapeDataString(payment.Reference.ToString())}";

        string? outcomeMessage = payment.Status switch
        {
            PaymentStatus.Failed => SwishOutcome.Resolve(SwishOutcome.Error, payment.ErrorCode).MemberMessage,
            PaymentStatus.Cancelled => SwishOutcome.Resolve(SwishOutcome.Cancelled, null).MemberMessage,
            _ => null,
        };

        return new SwishPaymentViewModel(
            payment.Reference,
            payment.AmountOre,
            payment.MembershipFeeOre,
            payment.FamilyFeeOre,
            payment.ClassFeeOre,
            payment.BookingId,
            trainingClass?.Title,
            booking?.ClassStartUtc,
            payment.Status,
            booking?.Status,
            booking?.HoldExpiresUtc,
            isMock,
            isStarted,
            appLink,
            QrUrl: isStarted && !isMock
                ? Url.SurfaceAction<SwishPaymentSurfaceController>(nameof(SwishPaymentSurfaceController.Qr)) + query
                : null,
            StatusUrl: isStarted
                ? Url.SurfaceAction<SwishPaymentSurfaceController>(nameof(SwishPaymentSurfaceController.Status)) + query
                : null,
            outcomeMessage);
    }
```

The constructor gains `IPaymentProvider paymentProvider, IPublishedContentQuery contentQuery, IPublishedUrlProvider publishedUrlProvider` as parameters after `MemberBookingsProvider bookingsProvider`. `Url.SurfaceAction<T>` is an Umbraco extension in `Umbraco.Extensions`, already imported for `Url(...)`.

- [ ] **Step 2: Rewrite the view**

Replace everything inside `<article class="post">` in `Views/SwishPayment.cshtml` with:

```cshtml
        @if (TempData["PaymentError"] is string paymentError)
        {
            <p class="form-notice form-notice--error" role="alert">@paymentError</p>
        }

        @if (pay is null)
        {
            @* Also what an unowned or unknown reference gets: saying "not yours" would let a
               reference be probed for existence. *@
            <h1>Betalningen hittades inte</h1>
            <p class="form-notice form-notice--error" role="alert">
                Vi kan inte hitta den betalningen. Prova att boka träningen igen.
            </p>
            @if (portalPage is not null)
            {
                <p><a href="@portalPage.Url()" class="btn-primary">Till mina sidor</a></p>
            }
        }
        else if (pay.IsPaid)
        {
            <h1>Klart!</h1>
            @if (pay.CreditIssued)
            {
                <p class="form-notice form-notice--ok" role="status">
                    Betalningen är genomförd. Platsen hann ta slut medan betalningen genomfördes, så du
                    har fått en tillgodoträning att boka en annan träning med.
                </p>
            }
            else if (pay.BookingId is null)
            {
                <p class="form-notice form-notice--ok" role="status">Betalningen är genomförd. Kontot är nu ett familjekonto.</p>
            }
            else
            {
                <p class="form-notice form-notice--ok" role="status">Betalningen är genomförd och din träning är bokad.</p>
            }
            @if (portalPage is not null)
            {
                <p><a href="@portalPage.Url()" class="btn-primary">Till mina sidor</a></p>
            }
        }
        else if (pay.IsPending is false)
        {
            <h1>Betalningen genomfördes inte</h1>
            <p class="form-notice form-notice--error" role="alert">@pay.OutcomeMessage</p>
            @if (portalPage is not null)
            {
                <p><a href="@portalPage.Url()" class="btn-primary">Till mina sidor</a></p>
            }
        }
        else
        {
            <div class="swish" @(pay.IsStarted ? "data-swish-started" : null) data-status-url="@pay.StatusUrl" data-poll-interval="3000">
                <div class="swish__head">
                    <img src="~/static/img/swish-logo.png" alt="Swish" class="swish__logo" />
                    @if (pay.IsMock)
                    {
                        <span class="swish__demo">Demoläge</span>
                    }
                </div>

                <h1 class="swish__amount">@Kr(pay.AmountOre) kr</h1>

                @{
                    @* A family upgrade is bought on its own, with no class attached, so the
                       "Tillgodoträning" fallback below would be nonsense for it. *@
                    var isUpgradeOnly = pay.BookingId is null;
                }

                <ul class="swish__lines">
                    @if (pay.MembershipFeeOre > 0)
                    {
                        <li><span>Årsavgift</span><span>@Kr(pay.MembershipFeeOre) kr</span></li>
                    }
                    @if (pay.FamilyFeeOre > 0)
                    {
                        <li><span>Familjetillägg</span><span>@Kr(pay.FamilyFeeOre) kr</span></li>
                    }
                    @if (pay.ClassFeeOre > 0)
                    {
                        <li><span>Träningsavgift</span><span>@Kr(pay.ClassFeeOre) kr</span></li>
                    }
                    else if (isUpgradeOnly is false)
                    {
                        <li><span>Träningsavgift</span><span>Tillgodoträning</span></li>
                    }
                </ul>

                @if (isUpgradeOnly)
                {
                    <p class="swish__what">
                        Familjekonto i ett år. Datumet då medlemskapet går ut ändras inte.
                    </p>
                }

                @if (pay.ClassTitle is { Length: > 0 } title)
                {
                    <p class="swish__what">
                        @title@(pay.ClassStartUtc is { } start
                            ? $" · {SwedishTime.ToSwedish(start).ToString("d MMMM HH:mm", culture)}"
                            : "")
                    </p>
                }

                @if (pay.IsStarted is false)
                {
                    @* State 1: nothing exists at Swish yet. The member decides when it does. *@
                    <div class="swish__actions">
                        @using (Html.BeginUmbracoForm<SwishPaymentSurfaceController>(
                            nameof(SwishPaymentSurfaceController.Start)))
                        {
                            <input type="hidden" name="reference" value="@pay.Reference" />
                            <button type="submit" class="btn-primary">Betala med Swish</button>
                        }
                    </div>
                }
                else if (pay.IsMock)
                {
                    @* Stand-in for the real Swish QR code. Deliberately obviously fake. *@
                    <div class="swish__qr" aria-hidden="true">
                        <div class="swish__qr-inner">DEMO</div>
                    </div>
                }
                else
                {
                    @* State 2: on a phone, open the app; on a desktop, scan. The script hides the
                       one that does not apply and offers a link to swap. Without JavaScript both
                       show, which is correct if unpolished. *@
                    <div class="swish__device" data-device="mobile">
                        <a href="@pay.AppLink" class="btn-primary swish__app-link">Öppna Swish</a>
                        <p class="swish__hint">Godkänn betalningen i Swish. Du kommer tillbaka hit när det är klart.</p>
                    </div>
                    <div class="swish__device" data-device="desktop">
                        <img src="@pay.QrUrl" alt="QR-kod för Swish" class="swish__qr-image" width="220" height="220" />
                        <p class="swish__hint">Öppna Swish på din telefon, välj Skanna QR-kod och godkänn betalningen.</p>
                    </div>
                    <p>
                        <button type="button" class="consent-btn consent-btn--link" data-device-toggle hidden></button>
                    </p>
                    <p class="swish__waiting" role="status">Väntar på att du godkänner i Swish …</p>
                }

                <p class="swish__reference">
                    Referens<br /><code>@pay.Reference</code>
                </p>

                @* Only when a place is actually being held.

                   A family upgrade holds nothing - it buys a capability, not a seat - so it has no
                   HoldExpiresUtc, and MinutesLeft falls out as zero. Rendering it anyway put
                   "Platsen är reserverad i 0 minuter till" on the page, describing a reservation
                   that does not exist and implying a deadline that is not there.

                   Rendered server-side so it reads correctly with no JavaScript; the script below
                   then keeps it ticking rather than leaving a snapshot from page load. *@
                @if (pay.HoldExpiresUtc is not null)
                {
                    <p class="swish__hold" data-hold-expires="@pay.HoldExpiresIso">
                        Platsen är reserverad i @pay.MinutesLeft
                        @(pay.MinutesLeft == 1 ? "minut" : "minuter") till.
                    </p>
                }

                <div class="swish__actions" data-hold-actions>
                    @if (pay.IsStarted && pay.IsMock)
                    {
                        @using (Html.BeginUmbracoForm<SwishPaymentSurfaceController>(
                            nameof(SwishPaymentSurfaceController.SimulatePaid)))
                        {
                            <input type="hidden" name="reference" value="@pay.Reference" />
                            <button type="submit" class="btn-primary">Simulera betalning</button>
                        }

                        @using (Html.BeginUmbracoForm<SwishPaymentSurfaceController>(
                            nameof(SwishPaymentSurfaceController.SimulateCancelled)))
                        {
                            <input type="hidden" name="reference" value="@pay.Reference" />
                            <button type="submit" class="consent-btn consent-btn--link">Simulera avbrott</button>
                        }
                    }
                    else
                    {
                        @using (Html.BeginUmbracoForm<SwishPaymentSurfaceController>(
                            nameof(SwishPaymentSurfaceController.Cancel)))
                        {
                            <input type="hidden" name="reference" value="@pay.Reference" />
                            <button type="submit" class="consent-btn consent-btn--link">Avbryt</button>
                        }
                    }
                </div>

                @if (pay.IsMock)
                {
                    <p class="swish__note">
                        Swish är inte kopplat. Knapparna ovan står för det svar appen skulle ha
                        skickat tillbaka.
                    </p>
                }
            </div>

            @if (pay.HoldExpiresUtc is not null)
            {
                <script src="~/static/js/payment-countdown.js" defer></script>
            }
            @if (pay.IsStarted && pay.IsMock is false)
            {
                <script src="~/static/js/swish-payment.js" defer></script>
            }
        }
```

- [ ] **Step 3: Create `wwwroot/static/js/swish-payment.js`**

```javascript
// The started state of the Swish page: shows the right hand-over for the device, and polls until
// Swish has decided.
//
// The poll is the path that always works. Swish's callback may never reach the server - the
// production binding drops handshakes without SNI - but this page asking "har det hänt något?"
// every few seconds does not depend on it. The server asks Swish at most every five seconds
// however fast the page polls; the interval here is only how quickly the member sees the result.
(function () {
    'use strict';

    var root = document.querySelector('[data-swish-started]');
    if (!root) {
        return;
    }

    // --- which hand-over to show ---

    var isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent);
    var mode = isMobile ? 'mobile' : 'desktop';
    var toggle = root.querySelector('[data-device-toggle]');

    function apply() {
        var blocks = root.querySelectorAll('[data-device]');
        for (var i = 0; i < blocks.length; i++) {
            blocks[i].hidden = blocks[i].getAttribute('data-device') !== mode;
        }

        if (toggle) {
            toggle.hidden = false;
            toggle.textContent = mode === 'mobile'
                ? 'Visa QR-kod istället'
                : 'Öppna Swish på den här telefonen istället';
        }
    }

    if (toggle) {
        toggle.addEventListener('click', function () {
            mode = mode === 'mobile' ? 'desktop' : 'mobile';
            apply();
        });
    }

    apply();

    // --- poll ---

    var statusUrl = root.getAttribute('data-status-url');
    var interval = parseInt(root.getAttribute('data-poll-interval'), 10) || 3000;
    if (!statusUrl) {
        return;
    }

    var failures = 0;

    function poll() {
        fetch(statusUrl, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            })
            .then(function (result) {
                failures = 0;
                if (result && result.terminal) {
                    // The server renders the outcome; reloading is simpler and more honest than
                    // rebuilding the page here.
                    window.location.reload();
                    return;
                }
                window.setTimeout(poll, interval);
            })
            .catch(function () {
                // Back off, but never give up while the page is open: the member may be mid-BankID.
                failures++;
                window.setTimeout(poll, Math.min(interval * (failures + 1), 15000));
            });
    }

    window.setTimeout(poll, interval);
})();
```

- [ ] **Step 4: CSS**

In `site.css`, after the `.swish__note` rule, add:

```css
/* The started state: one hand-over block per device, the script hides the other. */
.swish__device {
    margin: 0 0 0.5rem;
}

.swish__app-link {
    display: block;
    width: 100%;
    text-align: center;
    font-size: 1.1rem;
}

.swish__qr-image {
    display: block;
    width: 220px;
    height: 220px;
    margin: 0 auto 0.75rem;
    border: 3px solid var(--primary);
    border-radius: 6px;
    background: #fff;
}

.swish__hint {
    font-size: 0.9rem;
    color: var(--muted);
    margin: 0 0 0.75rem;
}

.swish__waiting {
    font-weight: 600;
    color: var(--primary);
}

.swish__waiting::before {
    content: "";
    display: inline-block;
    width: 0.7em;
    height: 0.7em;
    margin-right: 0.5em;
    border: 2px solid var(--primary);
    border-right-color: transparent;
    border-radius: 50%;
    vertical-align: -0.1em;
    animation: swish-spin 0.9s linear infinite;
}

@keyframes swish-spin {
    to { transform: rotate(360deg); }
}
```

(The existing `prefers-reduced-motion` block already stops the animation.)

- [ ] **Step 5: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success. Razor is compiled at run time here, so the view is verified at the checkpoint.

- [ ] **Step 6: Commit**

```bash
git add Booking/Web/SwishPaymentController.cs Views/SwishPayment.cshtml wwwroot/static/js/swish-payment.js wwwroot/static/css/site.css
git commit -m "Render the payment page in its four states with app switch, QR and poll

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint for Carl (next relaunch), mock still active:** book a class. The page shows the breakdown and *Betala med Swish*; pressing it reloads with *Demoläge*, the DEMO square, the countdown restarted at 7 minutes, and the two simulate buttons. *Simulera betalning* confirms as before. Book again and press *Avbryt*: the portal shows "Betalningen avbröts" and the hold is gone.

**Checkpoint against the simulator:** in `appsettings.Secrets.json` set `NDSTK:Swish:Enabled` true, `PayeeAlias` `1234679304`, `CertificatePath` to the simulator's `.p12`, `CertificatePassword` `swish`. Relaunch: the log says `Payment provider: Swish, against https://mss.cpc.getswish.net/swish-cpcapi/`. Book, press *Betala med Swish*: the page shows a real QR image and, in the log, `Swish request … created`. Within about ten seconds the page reloads to "Klart!" with no callback having arrived. Set `SimulateErrorCode` to `RF07`, book again: the page ends at "Betalningen genomfördes inte" with the bank-declined sentence, and the place is released.

---

## Phase 3 — Callback and reconciliation

### Task 14: The callback endpoint

**Files:**
- Create: `Booking/Web/SwishCallbackController.cs`

**Interfaces:**
- Consumes: `IBookingRepository.GetPaymentByProviderReferenceAsync` (Task 7), `BookingService.ReconcileAsync` (Task 11), `BookingRateLimits.Callback` (Task 12), `SwishCallbackUrl.Path` (Task 12).

- [ ] **Step 1: Create the controller**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Payments;
using NDSTK.Booking.Payments.Swish;
using NDSTK.Booking.Services;

namespace NDSTK.Booking.Web;

/// <summary>
/// Where Swish posts the outcome of a payment request.
/// </summary>
/// <remarks>
/// The body is read for one thing: which request it is about. Everything else about the payment
/// is then fetched from Swish over mTLS by <see cref="BookingService.ReconcileAsync"/>, so a forged
/// body cannot settle anything. The callbackIdentifier header, which only Swish and this server
/// know, must match the stored value first; a mismatch is logged and otherwise ignored.
///
/// Answers 200 for everything except a malformed body. A non-200 buys ten retries of the same
/// request, which would be useless for an unknown id and merely noisy for a duplicate.
///
/// Anonymous by design; attribute-routed like <c>MemberAdminController</c>, so it needs no
/// routing changes.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route(SwishCallbackUrl.Path)]
public sealed class SwishCallbackController(
    IBookingRepository repository,
    BookingService bookings,
    ILogger<SwishCallbackController> logger) : ControllerBase
{
    /// <summary>The one field of Swish's payment request object this endpoint reads.</summary>
    public sealed record CallbackBody([property: JsonPropertyName("id")] string? Id);

    [HttpPost]
    [EnableRateLimiting(BookingRateLimits.Callback)]
    public async Task<IActionResult> Receive([FromBody] CallbackBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Id) || body.Id.Length > 36)
        {
            return BadRequest();
        }

        PaymentRecord? payment = await repository.GetPaymentByProviderReferenceAsync(body.Id.Trim());
        var presented = Request.Headers["callbackIdentifier"].FirstOrDefault();

        if (payment is null || Matches(payment.CallbackIdentifier, presented) is false)
        {
            logger.LogWarning(
                "A Swish callback for request {InstructionId} was not accepted: {Reason}.",
                body.Id, payment is null ? "unknown request" : "callback identifier mismatch");
            return Ok();
        }

        try
        {
            await bookings.ReconcileAsync(payment, DateTime.UtcNow);
            logger.LogInformation("Swish callback for request {InstructionId} reconciled.", body.Id);
        }
        catch (PaymentProviderException exception)
        {
            // Swish just told us something and now cannot be asked about it. The page's poll or
            // the job will ask again; a 500 here would only make Swish repeat the callback.
            logger.LogWarning(exception, "Reconciling after the callback for {InstructionId} failed.", body.Id);
        }

        return Ok();
    }

    /// <summary>Constant-time comparison. Different lengths compare unequal without leaking which.</summary>
    private static bool Matches(string? stored, string? presented)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(stored);
        var b = Encoding.UTF8.GetBytes(presented);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
```

- [ ] **Step 2: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Booking/Web/SwishCallbackController.cs
git commit -m "Receive Swish callbacks, verify them, and reconcile rather than trust them

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint for Carl (next relaunch):** with the site running locally, this reproduces both branches without Swish:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://localhost:44351/api/swish/callback -H "Content-Type: application/json" -d "{\"id\":\"ABC\"}"
```

prints `200` and the log shows `not accepted: unknown request`. With no `id` in the body it prints `400`.

---

### Task 15: Reconcile in the job, before the sweep

**Files:**
- Modify: `Booking/Jobs/ClassReminderJob.cs`

**Interfaces:**
- Consumes: `IBookingRepository.GetPaymentsAwaitingReconciliationAsync` (Task 7), `BookingService.ReconcileAsync` (Task 11).

- [ ] **Step 1: Add the step**

In `RunJobAsync`, resolve the service and call the new step first:

```csharp
        using IServiceScope scope = scopeFactory.CreateScope();
        IBookingRepository repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<MembershipSettingsService>();
        var mail = scope.ServiceProvider.GetRequiredService<BookingMailService>();
        var classes = scope.ServiceProvider.GetRequiredService<TrainingClassService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        DateTime nowUtc = DateTime.UtcNow;

        await ReconcilePaymentsAsync(repository, bookings, nowUtc);
        await SweepExpiredHoldsAsync(repository, nowUtc);
        await SendRemindersAsync(repository, mail, classes, settings, nowUtc);
```

Then add the method before `SweepExpiredHoldsAsync`:

```csharp
    /// <summary>
    /// Asks Swish about every pending payment that has a request and is older than a minute. This
    /// is what catches a lost callback for a member who closed the tab after paying.
    /// </summary>
    /// <remarks>
    /// Before the sweep, deliberately. Sweeping first would expire the booking of a payment that
    /// turns out to be PAID a moment later; the late-payment rule would still recover it, but only
    /// by re-checking capacity or issuing a credit, when simply confirming was available.
    /// </remarks>
    private async Task ReconcilePaymentsAsync(
        IBookingRepository repository, BookingService bookings, DateTime nowUtc)
    {
        IReadOnlyList<PaymentRecord> awaiting =
            await repository.GetPaymentsAwaitingReconciliationAsync(nowUtc.AddMinutes(-1));

        if (awaiting.Count == 0)
        {
            return;
        }

        var settled = 0;

        foreach (PaymentRecord payment in awaiting)
        {
            try
            {
                PaymentRecord after = await bookings.ReconcileAsync(payment, nowUtc);
                if (after.Status != PaymentStatus.Pending)
                {
                    settled++;
                }
            }
            catch (PaymentProviderException exception)
            {
                // One unreachable call must not stop the rest, or the sweep and the reminders.
                logger.LogWarning(exception, "Reconciling payment {Reference} failed; next run.", payment.Reference);
            }
        }

        logger.LogInformation(
            "Reconciled {Count} pending Swish payment(s); {Settled} reached a final state.",
            awaiting.Count, settled);
    }
```

Add `using NDSTK.Booking.Payments;` to the usings.

- [ ] **Step 2: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Booking/Jobs/ClassReminderJob.cs
git commit -m "Reconcile pending Swish payments from the job before sweeping holds

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint against the simulator:** start a payment, close the tab, wait for the next job run (the log line `Reminder run starting`). The run logs `Reconciled 1 pending Swish payment(s); 1 reached a final state` and the booking is Confirmed in the backoffice.

---

### Task 16: The Swish reference in the backoffice

**Files:**
- Modify: `Booking/Admin/MemberAdminDetail.cs:13-21`
- Modify: `Booking/Admin/MemberAdminQueries.cs:97-104`
- Modify: `App_Plugins/NDSTK.MemberAdmin/members-dashboard.js:373-390`
- Modify: `App_Plugins/NDSTK.MemberAdmin/lang/sv.js:64`, `lang/en.js:76`

- [ ] **Step 1: Two properties on the row**

```csharp
public sealed record AdminPaymentRow(
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    int AmountOre,
    int MembershipFeeOre,
    int FamilyFeeOre,
    int ClassFeeOre,
    string Status,
    string Provider,
    /// <summary>Swish's payment reference once paid: what the bank statement shows. Null for the mock.</summary>
    string? BankReference,
    /// <summary>Swish's error code for a failed payment.</summary>
    string? ErrorCode);
```

- [ ] **Step 2: Select them**

In `MemberAdminQueries.cs` the SELECT becomes:

```csharp
            SELECT CreatedUtc, CompletedUtc, AmountOre, MembershipFeeOre, FamilyFeeOre,
                   ClassFeeOre, Status, Provider, BankReference, ErrorCode
            FROM {BookingTables.Payment}
```

- [ ] **Step 3: One column in the dashboard**

In `members-dashboard.js`, add a head cell after `colStatus`:

```javascript
                            <uui-table-head-cell>${this.#t('colSwishReference')}</uui-table-head-cell>
```

and in the row, replace the status cell and add the reference cell:

```javascript
                                <uui-table-cell>${p.status}${p.errorCode ? ` (${p.errorCode})` : ''}</uui-table-cell>
                                <uui-table-cell><code>${p.bankReference ?? '–'}</code></uui-table-cell>
```

- [ ] **Step 4: Language keys**

`lang/sv.js`, after `colStatus: 'Status',`: `colSwishReference: 'Swish-referens',`
`lang/en.js`, after `colStatus: 'Status',`: `colSwishReference: 'Swish reference',`

- [ ] **Step 5: Type-check**

Run: `dotnet build -t:"ResolveReferences;CoreCompile"`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add Booking/Admin/MemberAdminDetail.cs Booking/Admin/MemberAdminQueries.cs App_Plugins/NDSTK.MemberAdmin/members-dashboard.js App_Plugins/NDSTK.MemberAdmin/lang/sv.js App_Plugins/NDSTK.MemberAdmin/lang/en.js
git commit -m "Show the Swish reference and error code on the member's payments

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Checkpoint for Carl:** open a member in Medlemmar; the payments table has a *Swish-referens* column, filled for simulator payments and `–` for mock ones.

---

## Phase 4 — Documentation

### Task 17: README

**Files:**
- Modify: `README.md:67-68` (the flow sentence), `README.md:239-245` (the "Swish is mocked" section)

- [ ] **Step 1: The flow sentence**

Lines 67-68 become:

```markdown
Register → confirm the emailed link → sign in → book a class for one of your children → pay
through Swish, or through the mocked Swish page when no certificate is configured.
```

- [ ] **Step 2: Replace the "Swish is mocked" section**

Replace the whole section (heading through "…signed-in member.") with:

````markdown
## Swish

Two `IPaymentProvider`s. `SwishPaymentProvider` speaks Swish Commerce v2 over mTLS;
`SwishMockPaymentProvider` is the page with the two simulate buttons. `PaymentProviderFactory`
picks one at startup from `NDSTK:Swish`: Swish when `Enabled` is true, `PayeeAlias` is set and the
certificate loads, the mock otherwise. **The boot log says which**, at warning level for the mock,
so a site that is not taking money says so on its first page.

```json
{ "NDSTK": { "Swish": {
    "Enabled": true,
    "PayeeAlias": "123XXXXXXX",
    "CertificatePath": "D:\\secrets\\swish.p12",
    "CertificatePassword": "…" } } }
```

Those four belong in `appsettings.Secrets.json` or environment variables
(`NDSTK__Swish__CertificatePassword`), never in a committed file. `ApiBaseUrl` is production in
`appsettings.json` and the Merchant Swish Simulator in `appsettings.Development.json`.
`CertificateThumbprint` is the alternative to a file: a certificate in `LocalMachine\My`.

### How a payment runs

Booking writes a Pending payment and sends the member to the payment page, which shows the
breakdown and *Betala med Swish*. Nothing exists at Swish until they press it. `Start` then PUTs
the request, stores Swish's identifiers on the row and **restarts the hold at the configured
minutes**, so the reservation outlives Swish's own 5.5-minute timeout however long they looked
at the page first. On a phone the page offers *Öppna Swish*; on a desktop, a QR code from Swish's
generator. Either way the page polls `Status` every three seconds.

**The truth about a payment is what Swish says when asked over mTLS**, never what arrives in a
callback. `BookingService.ReconcileAsync` asks and applies the answer, and three things call it:

- the page's poll, at most every five seconds per payment;
- `SwishCallbackController` at `/api/swish/callback`, once the `callbackIdentifier` header
  matches the value stored for that request - a mismatch is logged and ignored;
- `ClassReminderJob`, for every pending payment older than a minute, **before** it sweeps holds.

So a lost callback costs nothing but a few seconds, and the integration works even where the
callback cannot arrive at all.

**Settlement is one conditional write.** `TryCompletePaymentAsync` moves a payment out of Pending
only if it is still there; the side effects - confirming the booking, extending the membership,
stamping the welcome price, setting the family flag - run only for the caller whose write changed
a row. Swish retries callbacks ten times, and the poll and the job race them; exactly one wins.

**Money that arrives after the hold lapsed** re-confirms the booking if the class still has room
(the capacity test is in the `UPDATE`'s `WHERE`, like the reservation's) and otherwise issues a
credit, exactly as a cancellation would. No refunds, as everywhere else in the model.

Message text, amount format, instruction id and the Swedish sentence for each Swish error code are
pure functions in `NDSTK.Domain` (`SwishRequest`, `SwishOutcome`), tested. The message is built
from the class, never typed, so no title can carry a character Swish rejects.

### Against the simulator

Point `ApiBaseUrl` at `https://mss.cpc.getswish.net/swish-cpcapi/` with the simulator's test
certificate (`Swish_Merchant_TestCertificate_1234679304.p12`, password `swish`, payee alias
`1234679304`). The simulator answers PAID about four seconds after create; its callback never
reaches a developer machine, and the poll settles the payment anyway. Set
`NDSTK:Swish:SimulateErrorCode` to `RF07`, `TM01`, `DS24` or `BANKIDCL` to see each failure
sentence. That setting is read only in the Development environment.

### Before go-live

1. The club signs **Swish Handel** with its bank and names a certificate contact.
2. The contact generates a 4096-bit RSA key and CSR, obtains the certificate at portal.swish.nu,
   and exports key and chain as a password-protected PKCS#12 outside the web root.
3. **The IIS binding for ndstk.se must not require SNI.** Swish's callback client does not send
   a server name, and the binding as of September 2026 drops such handshakes with no
   certificate. Untick *Require Server Name Indication*, or add a binding on the IP with the same
   certificate. Verify with `openssl s_client -connect ndstk.se:443 -noservername`. Until then,
   payments still settle through the poll and the job; only the callback is lost.
4. Set `Enabled`, `PayeeAlias`, `CertificatePath` and `CertificatePassword` on the server. The
   boot log reads `Payment provider: Swish`.
5. Optionally restrict `/api/swish/callback` to Swish's address, 213.132.115.94, in IIS.
6. One real payment of the smallest class price, and a bank reference on the row in Medlemmar.

The certificate is loaded with `MachineKeySet`, not `EphemeralKeySet`: SChannel cannot present an
ephemeral key as a TLS client certificate.
````

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Document the Swish integration, the simulator, and the go-live checklist

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage.** Provider interface → Task 6, 9. Pure rules → Tasks 2, 3. Schema → Tasks 4, 5. Conditional settlement and late money → Task 7. Page states, surface actions, QR, app link, poll → Tasks 12, 13. Callback → Task 14. Job step and sweep cleanup → Tasks 15 and 7. Configuration and certificate → Task 10. Hold default → Task 8. Backoffice → Task 16. Documentation and checklist → Task 17. Error simulation setting → Tasks 9, 10.
- **Deviation from the spec, deliberate:** the mock's `RetrieveAsync` returns Created rather than reading the row, because the provider is a singleton and the repository is scoped; the simulate buttons settle directly, as they always did. Behaviour for members is identical.
- **Type consistency checked:** `TryCompletePaymentAsync(int, string, DateTime, string?, string?)` in Tasks 7 and 11; `PaymentStart(ProviderReference, Token, CallbackIdentifier)` in Tasks 6, 9, 11; `SettlePaymentAsync(PaymentRecord, string?)` in Tasks 7, 11, 12; `SwishCallbackUrl.Path` in Tasks 12, 14; `SwishHttpClientNames.Api/Qr` in Tasks 9, 10, 12.
