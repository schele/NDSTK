# Member Booking System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the site's dead "Bli medlem" call to action into a working member area where visitors register with a confirmed email address, book training classes through a mocked Swish payment, cancel for a credit rather than a refund, and are reminded 24 hours before a class — with class capacity administered from the backoffice.

**Architecture:** Front-end pages are Umbraco content nodes created by the existing code-first installer, so editors own copy and URLs. Classes are `trainingClass` content nodes; bookings, payments and credits live in three custom SQLite tables reached through NPoco. All business rules — pricing, capacity, credit selection, the reminder window, time zone conversion — are pure static functions with no Umbraco or database dependency, which is what the test suite covers. Umbraco services are called only from thin repositories, surface controllers and one background job.

**Tech Stack:** Umbraco CMS 18.1.1, .NET 10, SQLite via NPoco, ASP.NET Core MVC surface controllers, Razor views, xUnit.

**Spec:** [docs/superpowers/specs/2026-08-24-member-booking-design.md](../specs/2026-08-24-member-booking-design.md)

## Global Constraints

- **Umbraco 18.1.1 on .NET 10.** Nullable reference types enabled, implicit usings enabled.
- **`MigrationBase` does not exist in Umbraco 18.** Migrations derive from `Umbraco.Cms.Infrastructure.Migrations.AsyncMigrationBase` and override `protected override Task MigrateAsync()`.
- **`IMemberSignInManager` is in `Umbraco.Cms.Web.Common.Security`**, not `Umbraco.Cms.Core.Security`. `PasswordSignInAsync` takes a **username string**, never a `MemberIdentityUser`.
- **`Upgrader.ExecuteAsync(IMigrationPlanExecutor, ICoreScopeProvider, IKeyValueService)`** — async, and the scope provider parameter is `Umbraco.Cms.Core.Scoping.ICoreScopeProvider`.
- **Money is always integer öre.** No `decimal` reaches the database: SQLite maps `decimal` to `REAL` and floating point must not touch payment records. Öre values are named with an `Ore` suffix.
- **All stored instants are UTC.** Class start times entered by editors are Swedish local time and are converted on the way in. Column and property names holding UTC end in `Utc`.
- **Member-facing copy is Swedish.** Backoffice property names, code identifiers, comments and log messages are English, matching the existing `ContentModel` code.
- **The user commits manually.** No task contains a `git commit` step. Each task ends with a verification checkpoint instead, leaving a clean working tree for the user to review and commit.
- **Branch:** `feature/member-booking`, already created.
- **Prices** default to membership 150 kr, first class 100 kr, later classes 200 kr, reminder 24 hours ahead, payment hold 15 minutes — all editable on the Settings node.
- **Follow the existing installer pattern.** New document types, element types, data types and content are declared in `ContentModel/` with stable GUIDs in `NdstkKeys` and created create-if-missing, exactly as `NdstkContentModelInstaller` already does.

---

## File Structure

**Revised during Task 1.** The plan originally put the domain rules in `Booking/Domain/` inside
the web project. They live in a separate `NDSTK.Domain` class library instead, for two reasons
found while executing: a running site holds a file lock on `NDSTK.dll`, so a test suite that
builds the web project cannot run while you are looking at the site; and the spec's requirement
that these rules have no Umbraco or database dependency is worth having the compiler enforce
rather than trusting to discipline. The namespace is unchanged at `NDSTK.Booking.Domain`, so no
code in later tasks needs rewriting.

`NDSTK.csproj` sits at the repository root, so its `DefaultItemExcludes` must list every sibling
project directory (`NDSTK.Tests\**;NDSTK.Domain\**`) or the SDK's default globs pull their
sources into the web assembly and the build fails with duplicate assembly attributes. **Any
further project added beside it has to be added to that list too.**

Umbraco-dependent code still lives under `Booking/` in the web project, mirroring how
`ContentModel/` is already organised as a feature folder at the project root.

| File | Responsibility |
| --- | --- |
| `NDSTK.Domain/SwedishTime.cs` | Europe/Stockholm ↔ UTC conversion |
| `NDSTK.Domain/PriceList.cs` | The four price values, in öre |
| `NDSTK.Domain/MemberState.cs` | Membership expiry and discount flag |
| `NDSTK.Domain/BookingQuote.cs` | The result of pricing one booking |
| `NDSTK.Domain/Pricing.cs` | Pure quote calculation |
| `NDSTK.Domain/BookingSnapshot.cs` | The minimum of a booking the rules need |
| `NDSTK.Domain/Capacity.cs` | Remaining-places rule |
| `NDSTK.Domain/CreditSnapshot.cs` | The minimum of a credit the rules need |
| `NDSTK.Domain/Credits.cs` | Which credit to spend |
| `NDSTK.Domain/Reminders.cs` | Which bookings are due a reminder |
| `NDSTK.Domain/BookingStatus.cs` | Booking status constants |
| `Booking/Data/PaymentStatus.cs` | Payment status constants |
| `Booking/Data/BookingRecord.cs`, `PaymentRecord.cs`, `CreditRecord.cs` | NPoco POCOs |
| `Booking/Data/BookingTables.cs` | Table and column name constants |
| `Booking/Data/IBookingRepository.cs`, `BookingRepository.cs` | All SQL |
| `Booking/Data/Migrations/BookingMigrationPlan.cs`, `AddBookingTables.cs` | Schema |
| `Booking/Services/*` | Orchestration: bookings, payments, member profile, classes, mail |
| `Booking/Web/*` | Surface controllers and view models |
| `Booking/Jobs/ClassReminderJob.cs` | Recurring reminder and sweeper job |
| `Booking/Notifications/TrainingClassChangedHandler.cs` | Editor-change resync |
| `Booking/BookingComposer.cs` | DI registration |
| `NDSTK.Tests/` | xUnit suite over `NDSTK.Domain`, referencing that project only |

---

## Task 1: Test project and Swedish time conversion

**Files:**
- Create: `NDSTK.Tests/NDSTK.Tests.csproj`
- Create: `NDSTK.Tests/SwedishTimeTests.cs`
- Create: `NDSTK.Domain/SwedishTime.cs`
- Modify: `NDSTK.slnx`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class NDSTK.Booking.Domain.SwedishTime` with `DateTime ToUtc(DateTime swedishLocal)` and `DateTime ToSwedish(DateTime utc)`. Every later task converting a class start time uses these.

- [x] **Step 1: Create the domain library and test project, and wire them to the solution**

```bash
dotnet new classlib -n NDSTK.Domain -o NDSTK.Domain --framework net10.0
rm -f NDSTK.Domain/Class1.cs
dotnet new xunit -n NDSTK.Tests -o NDSTK.Tests --framework net10.0
rm -f NDSTK.Tests/UnitTest1.cs

dotnet add NDSTK.csproj reference NDSTK.Domain/NDSTK.Domain.csproj
dotnet add NDSTK.Tests/NDSTK.Tests.csproj reference NDSTK.Domain/NDSTK.Domain.csproj

dotnet sln NDSTK.slnx add NDSTK.Domain/NDSTK.Domain.csproj
dotnet sln NDSTK.slnx add NDSTK.Tests/NDSTK.Tests.csproj
```

The test project references `NDSTK.Domain` and **not** `NDSTK.csproj`. Referencing the web
project makes `dotnet test` fail with a file-lock error whenever the site is running locally.

Set `<RootNamespace>NDSTK.Booking.Domain</RootNamespace>` in `NDSTK.Domain.csproj`, and add both
sibling directories to the web project's excludes:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);NDSTK.Tests\**;NDSTK.Domain\**</DefaultItemExcludes>
```

- [x] **Step 2: Confirm the empty suite runs**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: builds `NDSTK.Domain` and `NDSTK.Tests` only, then reports "No test is available",
which is correct for an empty suite. If it builds `NDSTK` or reports duplicate assembly
attributes, the excludes or the project reference are wrong — fix that before going on.

- [x] **Step 3: Write the failing test**

Create `NDSTK.Tests/SwedishTimeTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class SwedishTimeTests
{
    // Sweden is UTC+1 in winter and UTC+2 in summer. An editor typing 18:00 means 18:00
    // in Sweden both times, so the UTC instant must differ by season - this is the bug
    // that would otherwise send every July reminder an hour early.
    [Fact]
    public void ToUtc_in_winter_subtracts_one_hour()
    {
        var result = SwedishTime.ToUtc(new DateTime(2026, 1, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 1, 15, 17, 0, 0, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ToUtc_in_summer_subtracts_two_hours()
    {
        var result = SwedishTime.ToUtc(new DateTime(2026, 7, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToSwedish_round_trips_a_summer_instant()
    {
        var utc = SwedishTime.ToUtc(new DateTime(2026, 7, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 18, 0, 0), SwedishTime.ToSwedish(utc));
    }

    // A value that already claims to be UTC must not be shifted a second time.
    [Fact]
    public void ToUtc_leaves_an_instant_already_marked_utc_alone()
    {
        var utc = new DateTime(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal(utc, SwedishTime.ToUtc(utc));
    }
}
```

- [x] **Step 4: Run the test to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: FAIL to compile — `The type or namespace name 'SwedishTime' could not be found`.

- [x] **Step 5: Write the implementation**

Create `NDSTK.Domain/SwedishTime.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Converts between the Swedish wall-clock time an editor types into the backoffice date
/// picker and the UTC instants stored in the booking tables. The date picker returns a value
/// with no offset, so without this every reminder would be one or two hours out depending on
/// the season.
/// </summary>
public static class SwedishTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime ToUtc(DateTime swedishLocal)
    {
        if (swedishLocal.Kind == DateTimeKind.Utc)
        {
            return swedishLocal;
        }

        DateTime unspecified = DateTime.SpecifyKind(swedishLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public static DateTime ToSwedish(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>
    /// .NET 10 accepts IANA ids on Windows through ICU, and this project opts in to app-local
    /// ICU. The Windows id is still tried as a fallback so a host with ICU disabled degrades to
    /// the right zone rather than throwing at startup.
    /// </summary>
    private static TimeZoneInfo ResolveZone()
    {
        foreach (string id in new[] { "Europe/Stockholm", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next id.
            }
        }

        throw new InvalidOperationException(
            "Neither 'Europe/Stockholm' nor 'W. Europe Standard Time' is available on this host.");
    }
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 4 tests.

- [x] **Step 7: Verification checkpoint**

Run: `dotnet build -t:CoreCompile -v q --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Use `-t:CoreCompile` throughout this plan — a plain `dotnet build` fails to copy the output executable whenever the site is running locally, which is a file lock, not a compile error.

Report to the user: test project created and wired into `NDSTK.slnx`, 4 time zone tests passing. Working tree ready to commit.

---

## Task 2: Pricing engine

**Files:**
- Create: `NDSTK.Domain/PriceList.cs`
- Create: `NDSTK.Domain/MemberState.cs`
- Create: `NDSTK.Domain/BookingQuote.cs`
- Create: `NDSTK.Domain/Pricing.cs`
- Create: `NDSTK.Tests/PricingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record PriceList(int MembershipFeeOre, int FirstClassPriceOre, int ClassPriceOre)`
  - `record MemberState(DateOnly? MembershipPaidUntil, bool FirstClassDiscountUsed)`
  - `record BookingQuote(int MembershipDueOre, int ClassFeeOre)` with computed `int TotalOre` and `bool RequiresPayment`
  - `static BookingQuote Pricing.Quote(MemberState member, PriceList prices, bool useCredit, DateOnly today)`

- [x] **Step 1: Write the failing test**

Create `NDSTK.Tests/PricingTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class PricingTests
{
    private static readonly PriceList Prices = new(
        MembershipFeeOre: 15_000,
        FirstClassPriceOre: 10_000,
        ClassPriceOre: 20_000);

    private static readonly DateOnly Today = new(2026, 8, 24);

    private static MemberState Member(DateOnly? paidUntil, bool discountUsed = false)
        => new(paidUntil, discountUsed);

    [Fact]
    public void Brand_new_member_pays_membership_plus_the_discounted_first_class()
    {
        BookingQuote quote = Pricing.Quote(Member(null), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.ClassFeeOre);
        Assert.Equal(25_000, quote.TotalOre);
        Assert.True(quote.RequiresPayment);
    }

    [Fact]
    public void Paid_up_member_pays_only_the_full_class_price()
    {
        BookingQuote quote = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void Membership_expiring_today_is_still_valid()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today, discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
    }

    [Fact]
    public void Membership_that_expired_yesterday_is_charged_again()
    {
        BookingQuote quote = Pricing.Quote(
            Member(Today.AddDays(-1), discountUsed: true), Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
    }

    [Fact]
    public void Paid_up_member_spending_a_credit_owes_nothing_and_skips_payment()
    {
        BookingQuote quote = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: true), Prices, useCredit: true, Today);

        Assert.Equal(0, quote.TotalOre);
        Assert.False(quote.RequiresPayment);
    }

    [Fact]
    public void Lapsed_member_spending_a_credit_still_pays_the_membership_fee()
    {
        BookingQuote quote = Pricing.Quote(
            Member(null, discountUsed: true), Prices, useCredit: true, Today);

        Assert.Equal(15_000, quote.TotalOre);
        Assert.Equal(0, quote.ClassFeeOre);
        Assert.True(quote.RequiresPayment);
    }

    // The welcome price must survive being spent on a credit booking, otherwise cancelling
    // your first class silently costs you the discount as well as the money.
    [Fact]
    public void Spending_a_credit_does_not_consume_the_first_class_discount()
    {
        BookingQuote credited = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: false), Prices, useCredit: true, Today);
        Assert.Equal(0, credited.ClassFeeOre);

        BookingQuote next = Pricing.Quote(
            Member(new DateOnly(2027, 1, 1), discountUsed: false), Prices, useCredit: false, Today);
        Assert.Equal(10_000, next.ClassFeeOre);
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter PricingTests`
Expected: FAIL to compile — `PriceList`, `MemberState`, `BookingQuote` and `Pricing` are all undefined.

- [x] **Step 3: Write the domain records**

Create `NDSTK.Domain/PriceList.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The club's prices, in öre. Öre rather than kronor decimals because SQLite maps decimal to
/// REAL, and floating point has no business in a payment record.
/// </summary>
public sealed record PriceList(int MembershipFeeOre, int FirstClassPriceOre, int ClassPriceOre);
```

Create `NDSTK.Domain/MemberState.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The two facts about a member that affect what a booking costs. Both are stored as member
/// type properties so an administrator can comp a membership from the backoffice.
/// </summary>
/// <param name="MembershipPaidUntil">Inclusive last day of the paid membership; null when never paid.</param>
public sealed record MemberState(DateOnly? MembershipPaidUntil, bool FirstClassDiscountUsed);
```

Create `NDSTK.Domain/BookingQuote.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// What one booking costs, split so the payment page can show the member why.
/// </summary>
public sealed record BookingQuote(int MembershipDueOre, int ClassFeeOre)
{
    public int TotalOre => MembershipDueOre + ClassFeeOre;

    /// <summary>False when the total is zero, in which case the Swish step is skipped entirely.</summary>
    public bool RequiresPayment => TotalOre > 0;
}
```

- [x] **Step 4: Write the pricing rule**

Create `NDSTK.Domain/Pricing.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The whole pricing rule, as a pure function. Deliberately free of Umbraco and the database
/// so every combination of membership, discount and credit is cheap to test.
/// </summary>
public static class Pricing
{
    public static BookingQuote Quote(MemberState member, PriceList prices, bool useCredit, DateOnly today)
    {
        int membershipDueOre = IsMembershipValid(member, today) ? 0 : prices.MembershipFeeOre;

        int classFeeOre = useCredit
            ? 0
            : member.FirstClassDiscountUsed
                ? prices.ClassPriceOre
                : prices.FirstClassPriceOre;

        return new BookingQuote(membershipDueOre, classFeeOre);
    }

    /// <summary>The paid-until day is inclusive: a membership expiring today is still valid today.</summary>
    public static bool IsMembershipValid(MemberState member, DateOnly today)
        => member.MembershipPaidUntil is { } paidUntil && paidUntil >= today;
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter PricingTests`
Expected: PASS, 7 tests.

- [x] **Step 6: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected PASS, 11 tests total.
Run: `dotnet build -t:CoreCompile -v q --nologo` — expected 0 errors.

Report to the user: pricing engine complete, all seven pricing rules from the spec covered including the two edge cases (membership expiring today is valid; spending a credit preserves the welcome price).

---

## Task 3: Capacity rule

**Files:**
- Create: `NDSTK.Domain/BookingStatus.cs`
- Create: `NDSTK.Domain/BookingSnapshot.cs`
- Create: `NDSTK.Domain/Capacity.cs`
- Create: `NDSTK.Tests/CapacityTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static class BookingStatus` with `const string Pending = "Pending"`, `Confirmed`, `Cancelled`, `Expired`
  - `record BookingSnapshot(int Id, Guid MemberKey, string Status, DateTime? HoldExpiresUtc, DateTime ClassStartUtc, DateTime? ReminderSentUtc)`
  - `static int Capacity.RemainingPlaces(int capacity, IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)`
  - `static bool Capacity.HoldsPlace(BookingSnapshot booking, DateTime nowUtc)`
  - `static bool Capacity.HasLiveBooking(IEnumerable<BookingSnapshot> bookings, Guid memberKey, DateTime nowUtc)`

- [x] **Step 1: Write the failing test**

Create `NDSTK.Tests/CapacityTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CapacityTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ClassStart = new(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Member = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static BookingSnapshot Booking(string status, DateTime? holdExpires = null, Guid? member = null)
        => new(1, member ?? Guid.NewGuid(), status, holdExpires, ClassStart, null);

    [Fact]
    public void An_empty_class_has_every_place_free()
        => Assert.Equal(8, Capacity.RemainingPlaces(8, [], Now));

    [Fact]
    public void Confirmed_bookings_take_places()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
        ];

        Assert.Equal(6, Capacity.RemainingPlaces(8, bookings, Now));
    }

    // An unpaid booking still holds the place while the member is on the Swish page.
    [Fact]
    public void An_unexpired_hold_takes_a_place()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Pending, Now.AddMinutes(5))];

        Assert.Equal(7, Capacity.RemainingPlaces(8, bookings, Now));
    }

    // ...but an abandoned one must not, or classes silently fill with ghosts.
    [Fact]
    public void An_expired_hold_releases_its_place()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Pending, Now.AddMinutes(-1))];

        Assert.Equal(8, Capacity.RemainingPlaces(8, bookings, Now));
    }

    [Fact]
    public void Cancelled_and_expired_bookings_do_not_take_places()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Cancelled),
            Booking(BookingStatus.Expired),
        ];

        Assert.Equal(8, Capacity.RemainingPlaces(8, bookings, Now));
    }

    [Fact]
    public void A_full_class_has_no_places_left()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed), Booking(BookingStatus.Confirmed)];

        Assert.Equal(0, Capacity.RemainingPlaces(2, bookings, Now));
    }

    // An editor may lower capacity below the places already taken. Existing bookings stand and
    // the count must not go negative, or the portal would render "-3 platser kvar".
    [Fact]
    public void Reducing_capacity_below_the_places_taken_never_goes_negative()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
        ];

        Assert.Equal(0, Capacity.RemainingPlaces(1, bookings, Now));
    }

    [Fact]
    public void A_member_with_a_confirmed_booking_has_a_live_booking()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed, member: Member)];

        Assert.True(Capacity.HasLiveBooking(bookings, Member, Now));
    }

    [Fact]
    public void A_member_whose_only_booking_was_cancelled_may_book_again()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Cancelled, member: Member)];

        Assert.False(Capacity.HasLiveBooking(bookings, Member, Now));
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CapacityTests`
Expected: FAIL to compile — `BookingStatus`, `BookingSnapshot` and `Capacity` are undefined.

- [x] **Step 3: Write the status constants**

Create `NDSTK.Domain/BookingStatus.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Booking statuses, as strings rather than an enum so the value stored in SQLite is readable
/// when someone opens the database by hand.
/// </summary>
public static class BookingStatus
{
    /// <summary>Holds a place while the member is on the payment page.</summary>
    public const string Pending = "Pending";

    /// <summary>Paid, or covered by a credit. The only status that receives reminders.</summary>
    public const string Confirmed = "Confirmed";

    /// <summary>Cancelled by the member. Produces a credit, never a refund.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>The hold ran out, or the member abandoned the payment.</summary>
    public const string Expired = "Expired";
}
```

- [x] **Step 4: Write the snapshot record**

Create `NDSTK.Domain/BookingSnapshot.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Just enough of a booking for the pure rules to work with, so capacity, reminders and the
/// one-live-booking check need no database.
/// </summary>
public sealed record BookingSnapshot(
    int Id,
    Guid MemberKey,
    string Status,
    DateTime? HoldExpiresUtc,
    DateTime ClassStartUtc,
    DateTime? ReminderSentUtc);
```

- [x] **Step 5: Write the capacity rule**

Create `NDSTK.Domain/Capacity.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Decides how many places a class has left. A place is taken by a confirmed booking, or by a
/// pending one whose payment hold has not yet run out.
/// </summary>
public static class Capacity
{
    public static bool HoldsPlace(BookingSnapshot booking, DateTime nowUtc) => booking.Status switch
    {
        BookingStatus.Confirmed => true,
        BookingStatus.Pending => booking.HoldExpiresUtc is null || booking.HoldExpiresUtc > nowUtc,
        _ => false,
    };

    /// <summary>
    /// Never negative: an editor is allowed to lower the capacity below the places already
    /// taken, and the existing bookings stand.
    /// </summary>
    public static int RemainingPlaces(int capacity, IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)
        => Math.Max(0, capacity - bookings.Count(booking => HoldsPlace(booking, nowUtc)));

    /// <summary>
    /// A member may hold at most one live booking per class. A cancelled or expired booking
    /// does not count, so rebooking a class you left is allowed.
    /// </summary>
    public static bool HasLiveBooking(IEnumerable<BookingSnapshot> bookings, Guid memberKey, DateTime nowUtc)
        => bookings.Any(booking => booking.MemberKey == memberKey && HoldsPlace(booking, nowUtc));
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CapacityTests`
Expected: PASS, 9 tests.

- [x] **Step 7: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected PASS, 20 tests total.
Run: `dotnet build -t:CoreCompile -v q --nologo` — expected 0 errors.

Report to the user: capacity rule complete, including expired holds releasing places and lowered capacity never rendering a negative count.

---

## Task 4: Credit selection and reminder window rules

**Files:**
- Create: `NDSTK.Domain/CreditSnapshot.cs`
- Create: `NDSTK.Domain/Credits.cs`
- Create: `NDSTK.Domain/Reminders.cs`
- Create: `NDSTK.Tests/CreditTests.cs`
- Create: `NDSTK.Tests/ReminderTests.cs`

**Interfaces:**
- Consumes: `BookingSnapshot`, `BookingStatus` from Task 3.
- Produces:
  - `record CreditSnapshot(int Id, Guid MemberKey, int? SpentOnBookingId)`
  - `static int Credits.CountUnspent(IEnumerable<CreditSnapshot> credits)`
  - `static CreditSnapshot? Credits.NextSpendable(IEnumerable<CreditSnapshot> credits)`
  - `static IReadOnlyList<BookingSnapshot> Reminders.Due(IEnumerable<BookingSnapshot> bookings, DateTime nowUtc, int hoursBefore)`
  - `static IReadOnlyList<BookingSnapshot> Reminders.ExpiredHolds(IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)`

- [x] **Step 1: Write the failing credit test**

Create `NDSTK.Tests/CreditTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CreditTests
{
    private static readonly Guid Member = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void No_credits_means_nothing_to_spend()
    {
        Assert.Equal(0, Credits.CountUnspent([]));
        Assert.Null(Credits.NextSpendable([]));
    }

    [Fact]
    public void An_unspent_credit_is_counted_and_offered()
    {
        CreditSnapshot[] credits = [new(1, Member, null)];

        Assert.Equal(1, Credits.CountUnspent(credits));
        Assert.Equal(1, Credits.NextSpendable(credits)!.Id);
    }

    [Fact]
    public void A_spent_credit_is_neither_counted_nor_offered()
    {
        CreditSnapshot[] credits = [new(1, Member, SpentOnBookingId: 99)];

        Assert.Equal(0, Credits.CountUnspent(credits));
        Assert.Null(Credits.NextSpendable(credits));
    }

    // Oldest first, so a member's credits are used in the order they were earned.
    [Fact]
    public void The_oldest_unspent_credit_is_offered_first()
    {
        CreditSnapshot[] credits = [new(7, Member, null), new(3, Member, null), new(5, Member, 12)];

        Assert.Equal(3, Credits.NextSpendable(credits)!.Id);
        Assert.Equal(2, Credits.CountUnspent(credits));
    }
}
```

- [x] **Step 2: Write the failing reminder test**

Create `NDSTK.Tests/ReminderTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class ReminderTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static BookingSnapshot Booking(
        int id, DateTime classStartUtc, string status = BookingStatus.Confirmed,
        DateTime? reminderSentUtc = null, DateTime? holdExpiresUtc = null)
        => new(id, Guid.NewGuid(), status, holdExpiresUtc, classStartUtc, reminderSentUtc);

    [Fact]
    public void A_class_inside_the_window_is_due()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(20))];

        Assert.Equal([1], Reminders.Due(bookings, Now, 24).Select(b => b.Id));
    }

    [Fact]
    public void A_class_beyond_the_window_is_not_yet_due()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(25))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    [Fact]
    public void A_class_that_has_already_started_is_not_reminded()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddMinutes(-1))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    // Idempotence: the job runs every 15 minutes, so a stamped booking must never resend.
    [Fact]
    public void An_already_reminded_booking_is_not_reminded_again()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(20), reminderSentUtc: Now.AddHours(-1))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    // An unpaid hold is not a booking, so it gets no reminder.
    [Fact]
    public void A_pending_booking_is_not_reminded()
    {
        BookingSnapshot[] bookings =
        [
            Booking(1, Now.AddHours(20), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(5)),
            Booking(2, Now.AddHours(20), BookingStatus.Cancelled),
        ];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    [Fact]
    public void Only_pending_bookings_whose_hold_ran_out_are_swept()
    {
        BookingSnapshot[] bookings =
        [
            Booking(1, Now.AddDays(3), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(-1)),
            Booking(2, Now.AddDays(3), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(5)),
            Booking(3, Now.AddDays(3), BookingStatus.Confirmed),
        ];

        Assert.Equal([1], Reminders.ExpiredHolds(bookings, Now).Select(b => b.Id));
    }
}
```

- [x] **Step 3: Run both tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter "CreditTests|ReminderTests"`
Expected: FAIL to compile — `CreditSnapshot`, `Credits` and `Reminders` are undefined.

- [x] **Step 4: Write the credit rules**

Create `NDSTK.Domain/CreditSnapshot.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// One booking credit. The link to the booking that spent it lives here and nowhere else, so
/// the two directions cannot drift apart.
/// </summary>
public sealed record CreditSnapshot(int Id, Guid MemberKey, int? SpentOnBookingId);
```

Create `NDSTK.Domain/Credits.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Chooses which credit to spend. Deciding is pure; the actual spend is a conditional UPDATE in
/// the repository, so two concurrent bookings cannot spend the same credit even though both
/// were offered it here.
/// </summary>
public static class Credits
{
    public static int CountUnspent(IEnumerable<CreditSnapshot> credits)
        => credits.Count(credit => credit.SpentOnBookingId is null);

    /// <summary>Oldest first, so credits are used in the order they were earned.</summary>
    public static CreditSnapshot? NextSpendable(IEnumerable<CreditSnapshot> credits)
        => credits
            .Where(credit => credit.SpentOnBookingId is null)
            .OrderBy(credit => credit.Id)
            .FirstOrDefault();
}
```

- [x] **Step 5: Write the reminder rules**

Create `NDSTK.Domain/Reminders.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// Selects the bookings a reminder run should act on. Kept pure so the window boundaries and
/// the no-resend guarantee are testable without a scheduler or a database.
/// </summary>
public static class Reminders
{
    /// <summary>
    /// Confirmed bookings whose class starts within the next <paramref name="hoursBefore"/>
    /// hours and which have not been reminded yet. Stamping each booking as it is sent is what
    /// makes a crashed run safe to repeat.
    /// </summary>
    public static IReadOnlyList<BookingSnapshot> Due(
        IEnumerable<BookingSnapshot> bookings, DateTime nowUtc, int hoursBefore)
    {
        DateTime windowEnd = nowUtc.AddHours(hoursBefore);

        return
        [
            .. bookings
                .Where(booking => booking.Status == BookingStatus.Confirmed)
                .Where(booking => booking.ReminderSentUtc is null)
                .Where(booking => booking.ClassStartUtc > nowUtc && booking.ClassStartUtc <= windowEnd)
                .OrderBy(booking => booking.ClassStartUtc),
        ];
    }

    /// <summary>Pending bookings whose payment hold has run out, so their place can be released.</summary>
    public static IReadOnlyList<BookingSnapshot> ExpiredHolds(
        IEnumerable<BookingSnapshot> bookings, DateTime nowUtc)
        =>
        [
            .. bookings.Where(booking =>
                booking.Status == BookingStatus.Pending
                && booking.HoldExpiresUtc is { } expires
                && expires <= nowUtc),
        ];
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter "CreditTests|ReminderTests"`
Expected: PASS, 10 tests.

- [x] **Step 7: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected PASS, 30 tests total.
Run: `dotnet build -t:CoreCompile -v q --nologo` — expected 0 errors.

Report to the user: every business rule in the spec is now implemented as a tested pure function — pricing, capacity, credits, the reminder window and the hold sweep. Phase 1's domain layer is complete; the remaining phases wire it to Umbraco. Working tree ready to commit.

---

## Task 5: Database tables and migration

**Files:**
- Create: `Booking/Data/BookingTables.cs`
- Create: `Booking/Data/BookingRecord.cs`
- Create: `Booking/Data/PaymentRecord.cs`
- Create: `Booking/Data/CreditRecord.cs`
- Create: `Booking/Data/PaymentStatus.cs`
- Create: `Booking/Data/Migrations/AddBookingTables.cs`
- Create: `Booking/Data/Migrations/BookingMigrationPlan.cs`
- Create: `Booking/Data/Migrations/BookingMigrationRunner.cs`
- Create: `Booking/BookingComposer.cs`

**Interfaces:**
- Consumes: `BookingStatus` from Task 3.
- Produces: the three POCOs with public settable properties matching the columns below; `BookingTables.Booking`, `.Payment`, `.Credit` name constants; `PaymentStatus.Pending/Paid/Failed/Cancelled`; `BookingMigrationRunner` as an `INotificationAsyncHandler<UmbracoApplicationStartedNotification>`.

- [x] **Step 1: Write the table name constants and payment statuses**

Create `Booking/Data/BookingTables.cs`:

```csharp
namespace NDSTK.Booking.Data;

/// <summary>Table names, in one place so the POCOs and the migration cannot disagree.</summary>
internal static class BookingTables
{
    internal const string Booking = "ndstkBooking";
    internal const string Payment = "ndstkPayment";
    internal const string Credit = "ndstkBookingCredit";
}
```

Create `Booking/Data/PaymentStatus.cs`:

```csharp
namespace NDSTK.Booking.Data;

/// <summary>Payment statuses, as readable strings for the same reason as BookingStatus.</summary>
public static class PaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
```

- [x] **Step 2: Write the three POCOs**

Create `Booking/Data/BookingRecord.cs`:

```csharp
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

[TableName(BookingTables.Booking)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class BookingRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(ClassKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_ClassKey")]
    public Guid ClassKey { get; set; }

    /// <summary>
    /// Denormalised from the class node so the reminder query is one indexed range scan and
    /// never touches the published cache. Resynced when an editor republishes the class.
    /// </summary>
    [Column(nameof(ClassStartUtc))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_ClassStartUtc")]
    public DateTime ClassStartUtc { get; set; }

    [Column(nameof(Status))]
    [Length(20)]
    public string Status { get; set; } = Domain.BookingStatus.Pending;

    [Column(nameof(PaymentId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? PaymentId { get; set; }

    [Column(nameof(HoldExpiresUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? HoldExpiresUtc { get; set; }

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    [Column(nameof(ConfirmedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ConfirmedUtc { get; set; }

    [Column(nameof(CancelledUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CancelledUtc { get; set; }

    [Column(nameof(ReminderSentUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReminderSentUtc { get; set; }
}
```

`Domain.BookingStatus.Pending` rather than a local `"Pending"` literal: the status strings must
have exactly one definition, or the POCO default and the migration's partial index could drift
apart and silently stop enforcing one-live-booking-per-class.

Create `Booking/Data/PaymentRecord.cs`:

```csharp
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

[TableName(BookingTables.Payment)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class PaymentRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>The value that appears in the payment page URL. Unique, and never guessable.</summary>
    [Column(nameof(Reference))]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_ndstkPayment_Reference")]
    public Guid Reference { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkPayment_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(BookingId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? BookingId { get; set; }

    /// <summary>Total charged, in öre. Integer because SQLite maps decimal to REAL.</summary>
    [Column(nameof(AmountOre))]
    public int AmountOre { get; set; }

    [Column(nameof(MembershipFeeOre))]
    public int MembershipFeeOre { get; set; }

    [Column(nameof(ClassFeeOre))]
    public int ClassFeeOre { get; set; }

    [Column(nameof(Status))]
    [Length(20)]
    public string Status { get; set; } = PaymentStatus.Pending;

    [Column(nameof(Provider))]
    [Length(50)]
    public string Provider { get; set; } = string.Empty;

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    [Column(nameof(CompletedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CompletedUtc { get; set; }
}
```

Create `Booking/Data/CreditRecord.cs`:

```csharp
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

/// <summary>
/// A booking credit, issued when a member cancels. A ledger rather than a counter on the member:
/// spending is a conditional UPDATE that cannot double-spend, and the rows are an audit trail.
/// </summary>
[TableName(BookingTables.Credit)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class CreditRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBookingCredit_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(SourceBookingId))]
    public int SourceBookingId { get; set; }

    /// <summary>Null means unspent. The only link between a credit and the booking it paid for.</summary>
    [Column(nameof(SpentOnBookingId))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? SpentOnBookingId { get; set; }

    [Column(nameof(IssuedUtc))]
    public DateTime IssuedUtc { get; set; }

    [Column(nameof(SpentUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? SpentUtc { get; set; }
}
```

- [x] **Step 3: Write the migration**

Create `Booking/Data/Migrations/AddBookingTables.cs`:

```csharp
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Creates the three booking tables. Note AsyncMigrationBase, not MigrationBase - the latter
/// does not exist in Umbraco 18.
/// </summary>
internal sealed class AddBookingTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        CreateIfMissing<BookingRecord>(BookingTables.Booking);
        CreateIfMissing<PaymentRecord>(BookingTables.Payment);
        CreateIfMissing<CreditRecord>(BookingTables.Credit);

        // A member may hold at most one live booking per class. Expressed as a partial unique
        // index so a cancelled booking does not block rebooking the same class. The expression
        // builder has no partial-index support, hence raw SQL - SQLite has supported this since
        // 3.8 and the site runs SQLite.
        Database.Execute($"""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ndstkBooking_OneLivePerMemberClass
            ON {BookingTables.Booking} (MemberKey, ClassKey)
            WHERE Status IN ('{BookingStatus.Pending}', '{BookingStatus.Confirmed}')
            """);

        return Task.CompletedTask;
    }

    private void CreateIfMissing<T>(string tableName)
    {
        if (TableExists(tableName))
        {
            Logger.LogDebug("Table {TableName} already exists; skipping.", tableName);
            return;
        }

        Create.Table<T>().Do();
    }
}
```

Notes for the implementer, all verified by compiling against Umbraco 18.1.1:

- `BookingStatus` is in `NDSTK.Booking.Domain`, so add `using NDSTK.Booking.Domain;`.
- `TableExists`, `Create`, `Database` and `Logger` are all inherited from `AsyncMigrationBase`.
  `TableExists` is absent from the shipped XML documentation but does exist — do not replace it
  with a hand-rolled query.
- `Create.Table<T>().Do()` is the correct terminator; `Do()` is what executes the expression.
- `IndexTypes.UniqueNonClustered` and `NullSettings.Null` both exist despite being undocumented.
- `Logger.LogDebug` needs no `using Microsoft.Extensions.Logging;` because the Web SDK's implicit
  usings already include it. Add it anyway, to match the existing `ContentModel` files.

- [x] **Step 4: Write the migration plan and runner**

Create `Booking/Data/Migrations/BookingMigrationPlan.cs`:

```csharp
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

internal sealed class BookingMigrationPlan : MigrationPlan
{
    public BookingMigrationPlan() : base("NDSTK.Booking")
        => From(string.Empty).To<AddBookingTables>("booking-tables-1");
}
```

Create `Booking/Data/Migrations/BookingMigrationRunner.cs`:

```csharp
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Runs the booking migration plan once Umbraco is up, mirroring how
/// NdstkContentModelInstallHandler installs the content model. A failure is logged rather than
/// thrown: a broken schema should not take the whole site down.
/// </summary>
internal sealed class BookingMigrationRunner(
    IRuntimeState runtimeState,
    IMigrationPlanExecutor migrationPlanExecutor,
    ICoreScopeProvider scopeProvider,
    IKeyValueService keyValueService,
    ILogger<BookingMigrationRunner> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level is not RuntimeLevel.Run)
        {
            logger.LogInformation(
                "Skipping the booking migration; runtime level is {Level}.", runtimeState.Level);
            return;
        }

        try
        {
            var upgrader = new Upgrader(new BookingMigrationPlan());
            await upgrader.ExecuteAsync(migrationPlanExecutor, scopeProvider, keyValueService);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running the NDSTK booking migration failed.");
        }
    }
}
```

Note for the implementer: add `using Microsoft.Extensions.Logging;`.

- [x] **Step 5: Register the runner**

Create `Booking/BookingComposer.cs`:

```csharp
using NDSTK.Booking.Data.Migrations;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace NDSTK.Booking;

/// <summary>
/// Wires up the booking feature. Grows one registration at a time as the later tasks add
/// repositories, services, the reminder job and the editor-change handlers.
/// </summary>
public sealed class BookingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
        => builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, BookingMigrationRunner>();
}
```

- [x] **Step 6: Verify it compiles**

Run: `dotnet build -t:CoreCompile -v q --nologo`
Expected: `Build succeeded. 0 Error(s)`. This exact POCO and migration shape was compile-verified against Umbraco 18.1.1 while this plan was written, so a failure here means a typo, not a wrong API.

- [x] **Step 7: Verify the tables are actually created**

Stop any running site, then run the site once and confirm the schema exists:

```bash
dotnet run --no-build
```

Wait for `Application started`, stop it, then check:

```bash
sqlite3 umbraco/Data/Umbraco.sqlite.db ".tables" | tr ' ' '\n' | grep ndstk
```

Expected: `ndstkBooking`, `ndstkBookingCredit`, `ndstkPayment`.

If `sqlite3` is unavailable, confirm from the log instead — the run should contain no `Running the NDSTK booking migration failed` error, and a second run must not attempt the migration again (the key value store records it as done).

- [x] **Step 8: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected PASS, 30 tests.

Report to the user: three tables created and verified in the database, migration is idempotent across restarts, and the one-live-booking-per-class rule is enforced by a partial unique index rather than only in code. Phase 1 complete.

---

## Phase 2 preface: two gaps in the existing installer

Found while planning this phase, and both must be fixed before anything else in it works.

**`EnsureContentTypeAsync` cannot add properties to a type that already exists.** It is
create-if-missing by design — see the `configure` callback in `NdstkContentTypeFactory`,
which only runs for a brand new type, deliberately so backoffice edits survive a restart.
The `settings` document type already exists on the live site, so declaring new price fields
inside its `EnsureContentTypeAsync` call would silently do nothing. Task 6 adds a separate
*upgrade* method that adds only missing property groups and properties.

**`NdstkContentSeeder` returns immediately unless the content tree is empty.** So the new
member pages would appear only on a fresh database and never on the live site. Task 6 adds
an idempotent ensure-page helper that runs regardless of whether the tree is empty, keyed on
the stable GUIDs in `NdstkKeys`.

Both are additive: the existing create-if-missing and seed-once behaviour is untouched.

---

## Task 6: Installer upgrade capabilities

**Files:**
- Modify: `ContentModel/NdstkContentTypeFactory.cs`
- Modify: `ContentModel/NdstkKeys.cs`
- Create: `ContentModel/NdstkPageInstaller.cs`

**Interfaces** (as built — see the simplification note below):
- Consumes: the existing `NdstkContentTypeFactory` and `NdstkKeys`.
- Produces:
  - `Task<bool> NdstkContentTypeFactory.EnsureGroupAsync(Guid contentTypeKey, Guid groupKey, string groupAlias, string groupCaption, params IPropertyType[] properties)` — adds any property whose alias is missing, creating the group on the way. Returns true when it changed something.
  - `Task<bool> NdstkContentTypeFactory.EnsureMemberPropertiesAsync(string memberTypeAlias, string groupAlias, string groupCaption, params (IPropertyType Property, bool MemberCanView, bool MemberCanEdit)[] properties)`
  - `IContent? NdstkPageInstaller.EnsurePage(Guid key, string name, Guid parentKey, string documentTypeAlias, Action<IContent>? configureNew)` — create-if-missing by key, published on creation, existing node returned untouched. **Synchronous and nullable**: `IContentService` is synchronous, and null means the parent does not exist yet.
  - `NdstkKeys.MemberTypes.Member` = `d59be02f-1df9-4228-aa1e-01917d806cda` and `MemberTypes.MemberAlias = "Member"` (the key already in `uSync/v18/MemberTypes/member.config`).

**Simplification found while implementing.** The plan below proposed hand-manipulating
`PropertyGroups` and `PropertyTypeCollection` for document types. Decompiling `IContentTypeBase`
showed it already has `bool AddPropertyType(IPropertyType, string propertyGroupAlias, string?
propertyGroupName = null)` and `bool PropertyTypeExists(string?)` — the same pair used for member
types — which creates the group as a side effect. Both methods now use that instead, roughly
halving the code. One wrinkle it introduces: Umbraco assigns the new group a random key, which
would make a uSync export differ per environment, so `EnsureGroupAsync` overwrites the key with
the caller's stable `groupKey` when — and only when — it was the call that created the group.

Also note `contentTypeService.UpdateAsync` returns `Attempt<ContentTypeOperationStatus>` whose
status is on `.Result`, not `.Status` — matching the existing `SetAllowedChildrenAsync`. The
`.Status` property exists on the *template* and *data type* attempts, which is the trap.

- [x] **Step 1: Add the member type key to the key registry**

In `ContentModel/NdstkKeys.cs`, add alongside the existing nested classes:

```csharp
    /// <summary>
    /// The member type. The key is the one already in uSync/v18/MemberTypes/member.config, so
    /// the installer upgrades that member type rather than creating a second one.
    /// </summary>
    internal static class MemberTypes
    {
        internal static readonly Guid Member = new("d59be02f-1df9-4228-aa1e-01917d806cda");
    }
```

- [x] **Step 2: Add the group-upgrade method to the factory**

Append to `NdstkContentTypeFactory`:

```csharp
    /// <summary>
    /// Adds a property group, and any properties within it, to a document type that already
    /// exists. <see cref="EnsureContentTypeAsync"/> deliberately never touches an existing type,
    /// so this is the only way to roll a new field out to a site that is already installed.
    /// Properties are matched by alias, so a re-run is a no-op and an editor's own additions to
    /// the group survive.
    /// </summary>
    public async Task<bool> EnsureGroupAsync(
        Guid contentTypeKey,
        Guid groupKey,
        string alias,
        string caption,
        int sortOrder,
        params IPropertyType[] properties)
    {
        IContentType contentType = contentTypeService.Get(contentTypeKey)
                                   ?? throw new InvalidOperationException($"Content type {contentTypeKey} was not found.");

        PropertyGroup? group = contentType.PropertyGroups.FirstOrDefault(g => g.Alias == alias);
        var changed = false;

        if (group is null)
        {
            AddGroup(contentType, groupKey, alias, caption, sortOrder);
            group = contentType.PropertyGroups.First(g => g.Alias == alias);
            changed = true;
        }

        foreach (IPropertyType property in properties)
        {
            if (contentType.PropertyTypeExists(property.Alias))
            {
                continue;
            }

            group.PropertyTypes ??= new PropertyTypeCollection(true);
            group.PropertyTypes.Add(property);
            changed = true;
        }

        if (changed is false)
        {
            return false;
        }

        var attempt = await contentTypeService.UpdateAsync(contentType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not add group '{alias}' to '{contentType.Alias}': {attempt.Status}.");
        }

        return true;
    }
```

Note for the implementer: `AddGroup` is the existing static helper — call it with no properties
and add them below, so the same code path serves both the new-group and existing-group cases.
If `PropertyTypeExists` does not resolve on `IContentType`, fall back to
`contentType.PropertyTypes.Any(p => p.Alias == property.Alias)`.

- [x] **Step 3: Add the member type upgrade method**

`IMemberTypeService` derives from `IContentTypeBaseService<IMemberType>`, and `IMemberType`
carries the per-property member visibility flags. All of the following was compile-verified
against Umbraco 18.1.1:

```csharp
    /// <summary>
    /// Adds properties to the member type. The member type already exists (uSync created it), so
    /// this is an upgrade in the same sense as <see cref="EnsureGroupAsync"/>. The two visibility
    /// flags matter: the membership expiry and the discount flag are administrative facts, so a
    /// member may see them but never edit them.
    /// </summary>
    public async Task<bool> EnsureMemberPropertiesAsync(
        string memberTypeAlias,
        string groupAlias,
        string groupCaption,
        IReadOnlyList<(IPropertyType Property, bool MemberCanView, bool MemberCanEdit)> properties)
    {
        IMemberType memberType = memberTypeService.Get(memberTypeAlias)
                                 ?? throw new InvalidOperationException($"Member type '{memberTypeAlias}' was not found.");

        var changed = false;

        foreach ((IPropertyType property, bool canView, bool canEdit) in properties)
        {
            if (memberType.PropertyTypes.Any(existing => existing.Alias == property.Alias))
            {
                continue;
            }

            memberType.AddPropertyType(property, groupAlias, groupCaption);
            memberType.SetMemberCanViewProperty(property.Alias, canView);
            memberType.SetMemberCanEditProperty(property.Alias, canEdit);
            changed = true;
        }

        if (changed is false)
        {
            return false;
        }

        var attempt = await memberTypeService.UpdateAsync(memberType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not add properties to member type '{memberTypeAlias}': {attempt.Status}.");
        }

        return true;
    }
```

Add `IMemberTypeService memberTypeService` to the factory's primary constructor parameter list.

- [x] **Step 4: Create the idempotent page installer**

Create `ContentModel/NdstkPageInstaller.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Creates the pages a feature needs, if they are not there already. Unlike
/// <see cref="NdstkContentSeeder"/> - which fills a brand new site and does nothing at all once
/// the tree has content - this runs on every start, so a page added by a later feature reaches a
/// site that is already live. Matching is by key, so renaming or moving a page in the backoffice
/// does not cause a duplicate.
/// </summary>
internal sealed class NdstkPageInstaller(
    IContentService contentService,
    ILogger<NdstkPageInstaller> logger)
{
#pragma warning disable CS0618 // IContentService still only takes an integer user id.
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    public IContent? EnsurePageAsync(
        Guid key,
        string name,
        Guid parentKey,
        string documentTypeAlias,
        Action<IContent>? configureNew = null)
    {
        IContent? existing = contentService.GetById(key);
        if (existing is not null)
        {
            return existing;
        }

        IContent? parent = contentService.GetById(parentKey);
        if (parent is null)
        {
            logger.LogWarning(
                "Cannot create '{Name}': parent {ParentKey} does not exist yet.", name, parentKey);
            return null;
        }

        IContent page = contentService.Create(name, parent.Id, documentTypeAlias, UserId);
        page.Key = key;
        configureNew?.Invoke(page);
        contentService.Save(page, UserId);

        PublishResult result = contentService.Publish(page, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning("Created '{Name}' but could not publish it: {Status}.", name, result.Result);
        }

        logger.LogInformation("Created page '{Name}'.", name);
        return page;
    }
}
```

- [x] **Step 5: Register the page installer**

In `ContentModel/NdstkContentModelComposer.cs`, add beside the existing singletons:

```csharp
        builder.Services.AddSingleton<NdstkPageInstaller>();
```

- [x] **Step 6: Verify it compiles**

Run: `dotnet build -t:CoreCompile -v q --nologo`
Expected: 0 errors. Resolve any `PropertyTypeExists` / `PropertyTypes.Add` mismatch against the
real `IContentType` members before continuing — do not guess.

- [x] **Step 7: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected PASS, 31 tests (unchanged; this task
adds no rules).

Report to the user: the installer can now roll new fields and new pages out to a site that is
already live, which the original create-if-missing installer could not do.

---

## Task 7: Settings fields, member properties, profile service — DONE

**Files:**
- Modified: `ContentModel/NdstkContentModelInstaller.cs` — new `UpgradeExistingTypesAsync()` step
- Created: `Booking/Services/MembershipSettings.cs`, `MembershipSettingsService.cs`, `MemberProfileService.cs`
- Modified: `Booking/BookingComposer.cs`

**Interfaces produced:**
- `record MembershipSettings(PriceList Prices, int ReminderHoursBefore, int PaymentHoldMinutes)` with `MembershipSettings.Defaults` (150/100/200 kr, 24 h, 15 min).
- `MembershipSettingsService.Get()`, `.GetMemberPortalPage()`, `.GetRegisterPage()`.
- `MemberProfileService.GetStateAsync(Guid) -> Task<MemberState>`, `.ExtendMembershipAsync(Guid, DateOnly)`, `.MarkFirstClassDiscountUsedAsync(Guid)`.

**Backoffice fields added.** Settings gains a "Medlemskap" tab: `membershipFee`, `firstClassPrice`,
`classPrice`, `reminderHoursBefore`, `paymentHoldMinutes` (all Numeric), plus `memberPortalPage`
and `registerPage` (ContentPicker). The `Member` member type gains `membershipPaidUntil` (date) and
`firstClassDiscountUsed` (true/false), both member-viewable but **not** member-editable — a member
who could edit their own expiry date would have a free membership.

**Decisions worth keeping:**
- **Prices are entered in kronor, stored in öre.** Editors think in kronor; the ×100 happens in
  exactly one place, `MembershipSettingsService`, so there is no second site for a factor of a
  hundred to go missing.
- **Zero counts as "not set", per field.** A free class or a zero-minute payment hold is far more
  likely to be an empty field than a deliberate choice, and treating zero as deliberate would give
  classes away. Each field falls back independently, so a partly-filled Settings node still behaves.
- **An unknown member is treated as brand new**, not as an error — the caller is about to quote a
  price, and quoting the full joining price for an unknown member fails safe.

**API corrections found by decompiling** (all would have been runtime or compile failures):
- `IMemberService` has **no** `GetByKey`. It exposes `GetById(int)`, `GetByEmail`, `GetByUsername`
  and `Task<IEnumerable<IMember>> GetByKeysAsync(params Guid[])`. `MemberProfileService` is
  therefore async throughout.
- `IPublishedContentQuery` is in namespace `Umbraco.Cms.Core`, **not** `Umbraco.Cms.Core.PublishedCache`.
- `IPublishedContentCache` has only `GetByIdAsync`; there is no `GetAtRoot()`. Root content comes
  from `IPublishedContentQuery.ContentAtRoot()`, which is what `UmbracoHelper` uses internally and
  works outside a view.

**Runtime verification.** Booted twice. Run 1 logged "Added the Medlemskap group…" and "Added the
membership properties…"; run 2 logged neither and created no tables. Run 2's silence is the proof
the fields persisted, since `EnsureGroupAsync` only skips when `PropertyTypeExists` is true.

---

## Task 8: Mail — DONE

**Files:**
- Modified: `appsettings.json` (production SMTP), `appsettings.Development.json` (dev file drop)
- Created: `NDSTK.Domain/MailContent.cs`, `NDSTK.Domain/MailTemplates.cs`
- Created: `Booking/Services/BookingMailService.cs`
- Created: `NDSTK.Tests/MailTemplateTests.cs` (4 tests)
- Modified: `Booking/BookingComposer.cs`

**Interfaces produced:**
- `record MailContent(string Subject, string HtmlBody)`
- `static MailContent MailTemplates.Verification(string verificationUrl)`
- `Task BookingMailService.SendVerificationAsync(string toEmail, string verificationUrl)`

**Configuration.** `Umbraco:CMS:Global:Smtp` in `appsettings.json`: `send.one.com`, port 465,
`SslOnConnect`, from and username `info@ndstk.se`. **The password goes in
`appsettings.Secrets.json`** under the same `Umbraco:CMS:Global:Smtp:Password` path — gitignored,
already loaded by `Program.cs`. Development overrides `DeliveryMethod` to
`SpecifiedPickupDirectory` with `umbraco/Logs/Mail`.

**Three findings from decompiling `Umbraco.Cms.Infrastructure.Mail.EmailSender`:**
1. **`PickupDirectoryLocation` is checked *before* SMTP.** Setting it in production would write
   every mail silently to disk instead of sending it, so `appsettings.json` must never define it.
2. **The sender does not create the directory.** It calls `File.Open(path, FileMode.CreateNew)`,
   which throws if the folder is absent — and `umbraco/Logs/` is gitignored, so a fresh clone
   never has it. `BookingMailService.EnsurePickupDirectory` creates it before every send.
3. `CanSendRequiredEmail()` reports whether either transport is configured, which is what the
   service checks before attempting a send.

**`umbraco/Logs/Mail` was chosen over `umbraco/Data/Mail`** because `.gitignore` excludes
`/umbraco/Logs/` wholesale while `/umbraco/Data/` is only partly excluded — test mail containing
live verification tokens can therefore never be committed by accident.

**Plain strings, not Razor.** The spec called for Razor partials rendered to string. Rendering
Razor outside a request needs a synthetic `ActionContext` and `HttpContext`, and the Phase 6
reminder job has no request at all — the same mail would have to be produced two different ways.
Plain functions behave identically in a controller and in a background job, and being pure makes
the HTML escaping unit-testable, which matters because an Identity token is interpolated into an
`href` attribute. `MailTemplateTests` covers exactly that injection case.

**Not yet verified at runtime:** nothing triggers a send until registration exists. Task 9 checks
that an `.eml` lands in `umbraco/Logs/Mail`.

---

## Tasks 9 and 10: Registration and email verification — DONE

Built together, because registration cannot send a verification mail until the verify page exists.

**Files created:** `Booking/Web/RegisterFormModel.cs`, `RegisterSurfaceController.cs`,
`MemberVerifyController.cs`, `BookingRateLimits.cs`; `Views/MemberRegister.cshtml`,
`Views/MemberVerify.cshtml`; `ContentModel/NdstkMemberPages.cs`.
**Files modified:** `NdstkKeys.cs` (template/doctype/node keys), `NdstkContentModelInstaller.cs`
(two templates, two document types, allowed children), `NdstkContentModelInstallHandler.cs`,
`NdstkContentModelComposer.cs`, `Program.cs` (rate limiter), `wwwroot/static/css/site.css`.

### Three defects found by testing rather than by reading

**1. Account-enumeration leak in the original ordering.** The plan had the existence check before
account creation. `IMemberManager` does not expose `PasswordValidators`, so the password could not
be pre-validated — which meant an attacker could submit a deliberately weak password and read the
difference: "check your inbox" would mean the address exists, a password error would mean it is
free. Fixed by letting one `CreateAsync` call do the password policy and the uniqueness check
together, then reporting **password errors first** and treating duplicates exactly like success.
With a strong password the two cases are indistinguishable; with a weak one both fail identically.

**2. `AmbiguousMatchException` on every request to the verify page.** `RenderController.Index()` is
a `virtual IActionResult` — sync. An `async Task<IActionResult> Index(...)` beside it is an
*overload*, not an override, so MVC registered two endpoints with the same name. Umbraco's route
hijacking looks for an action named after the **template alias** before falling back to `Index`, so
the action is named `MemberVerify`. That gives an async action with no clash and no blocking.

**3. The rate limiter silently permitted everything.** `app.UseRateLimiter()` was placed before
`app.UseUmbraco()`. `RateLimitingMiddleware` reads the `[EnableRateLimiting]` policy from the
**matched endpoint's metadata**, so with no endpoint yet it finds no policy and lets every request
through — a security control that looks present and does nothing. Decompiling
`UmbracoApplicationBuilder` showed `RegisterDefaultRequiredMiddleware()` calls `UseRouting()`
*before* the `WithMiddleware` callback runs, so the limiter now lives inside that callback.
Verified: 10 requests pass, the 11th returns 429.

### Security measures, each verified

| Measure | Evidence |
| --- | --- |
| Antiforgery + `ufprt` route string | POST succeeds only with both hidden fields; `BeginUmbracoForm` supplies the token itself, so no duplicate is rendered |
| Enumeration resistance | Duplicate address returns the identical message to success and sends no mail |
| Password policy | Umbraco's `MemberPassword` config via `CreateAsync`, not a second policy invented here |
| Per-IP rate limit | 10 per 5 minutes, then 429 — confirmed live |
| Honeypot + minimum fill time | Off-screen field (not `display:none`, which some bots skip) plus a 2-second floor; a trip answers exactly as success does |
| Unapproved until verified | Account created with `isApproved: false`; Umbraco's own sign-in refuses unapproved members, so this holds even if the login check were bypassed |
| Token escaping | `Uri.EscapeDataString` on the base64 token, and `WebUtility.HtmlEncode` into the `href` — both covered by tests and confirmed in the sent mail |

### Runtime verification, end to end

Registered `carl.test@ndstk.se` over HTTPS: form accepted, success notice rendered, and one `.eml`
written to `umbraco/Logs/Mail` from `info@ndstk.se` with the Swedish subject and a correctly
escaped token (`+`→`%2B`, `/`→`%2F`, `=`→`%3D`). Following that link activated the account
("din e-postadress är bekräftad", plus the log line). Following it a second time returned
"redan aktiverat" rather than an error, which matters because some mail clients prefetch links.
ModelsBuilder then regenerated `Member` and `Settings` with all nine new properties, independently
confirming the schema landed.

**Verification ordering — hardened after review.** The first implementation checked "already
active" *before* validating the token, so an already-verified account answered "redan aktiverat" to
any token at all. That made the page an oracle: anyone holding a member's GUID could learn whether
the account was active.

Not exploitable as it stood — member keys are random v4 GUIDs, and the only ways to obtain one were
to receive that member's own mail or to be a backoffice admin, both of which already tell you more.
It was fixed anyway, because its safety rested on an invariant nothing enforces: *member GUIDs never
reach front-end output*. The booking tables are keyed by `MemberKey`, so that assumption has several
phases of code still to survive. Payments were given their own `Reference` GUID for the same reason.

The apparent trade-off — friendly repeat-click message versus a uniform error — turned out to be
false. Validating the token **first** and only then distinguishing already-active from
newly-confirmed gives both, because Identity's confirmation token stays valid until expiry
(`ConfirmEmailAsync` does not rotate the security stamp). Verified live:

| Case | Result |
| --- | --- |
| Fresh token, first click | "din e-postadress är bekräftad" |
| Same token, second click | "redan aktiverat" — friendly path intact |
| Tampered token, active account | generic error |
| Nonexistent member GUID | **identical** output to the row above — oracle closed |
| 25-minute-old token | still works; tokens are not invalidated by age or restarts |
| Register → failed login before verifying → then click the link | activates normally |

One loose end left deliberately: a re-click after the member has already signed in appears to fail
(`InvalidToken`), most likely because a successful sign-in updates `LastLoginDate` and rotates the
security stamp. The confirming test could not be run because the rate limiter — correctly — returned
429. Benign either way: the account is already active, so the link has nothing left to do.

**Left in the dev database on purpose:** the verified test member `carl.test@ndstk.se`, for
exercising login in Task 11.

---

## Task 11: Login and logout — DONE (Phase 2 complete)

**Files created:** `Booking/Web/LoginFormModel.cs`, `LoginSurfaceController.cs`,
`ContentModel/NdstkMemberContentUpgrade.cs`.
**Files modified:** `Views/Login.cshtml` (real form, replacing the BankID placeholder),
`Views/Root.cshtml` (sidebar reflects sign-in state), install handler and composer.

### The pre-password-check leak, and how it is closed

`PasswordSignInAsync` returns `IsNotAllowed` for an account that exists but may not sign in — for
us, one that has not been verified. Identity produces that from `PreSignInCheck`, **before the
password is verified**, so showing "activate your account" on `IsNotAllowed` alone would tell
anyone typing any password that the address is registered. The controller therefore calls
`CheckPasswordAsync` explicitly before showing that message, so only someone who already knows the
password learns anything.

Verified live, all four paths:

| Attempt | Response |
| --- | --- |
| Verified account, correct password | 302 to the portal (start page while no portal is picked), auth cookie set |
| Verified account, wrong password | "Fel e-postadress eller lösenord." |
| Address that does not exist, same wrong password | byte-identical output to the row above |
| Unverified account, **correct** password | "Kontot är inte aktiverat än…" |
| Unverified account, **wrong** password | "Fel e-postadress eller lösenord." — no leak |

### Other decisions

- **Logout is a POST with antiforgery**, not a link. A GET logout means any `<img>` or prefetched
  URL on another site can sign a member out.
- **`SignInResult` had to be fully qualified.** `Microsoft.AspNetCore.Identity.SignInResult` and
  `Microsoft.AspNetCore.Mvc.SignInResult` both exist and both namespaces are in scope in a surface
  controller.
- **Login redirect falls back to the start page** when no member portal is picked on Settings, so
  login works before Phase 3 creates the portal.
- **The stale BankID copy is replaced once, guarded by a key/value marker**, following the
  `NdstkLanguageInstaller` precedent. The guard is the point: this overwrites values an editor can
  also change, so without it every restart would undo their wording. `NdstkMemberContentUpgrade`
  also fills the `registerPage` picker on Settings, and will fill `memberPortalPage` in Phase 3.

**Left in the dev database:** `carl.test@ndstk.se` (verified) and `unverified@ndstk.se`
(deliberately unverified), for exercising both login paths.

---

## Tasks 12–14: Classes in the backoffice and the member portal — DONE (Phase 3 complete)

**Scope correction made at the start:** the portal shows *live* remaining places, which needs
booking counts from the database, so the read side of `IBookingRepository` moved from Phase 4 into
this phase.

**Files created:** `NDSTK.Domain/TrainingClass.cs`, `BookableClass.cs`;
`Booking/Data/IBookingRepository.cs`, `BookingRepository.cs`;
`Booking/Services/TrainingClassService.cs`; `Booking/Web/MemberPortalViewModel.cs`,
`MemberPortalController.cs`; `ContentModel/NdstkMemberAccessInstaller.cs`;
`Views/MemberPortal.cshtml`; `NDSTK.Tests/BookableClassTests.cs` (8 tests).
**Files modified:** keys, installer (portal + `trainingClasses` + `trainingClass` types),
`NdstkMemberPages.cs` (portal, folder, three example classes), `MemberVerifyController.cs` (group
assignment), `NdstkMemberContentUpgrade.cs` (marker bump), `BookingComposer.cs`, `site.css`.

### Backoffice class management

`trainingClass` under a `Träningar` folder, with `title`, `description`, `start`
(**date and time** — `DatePickerWithTime`, `e4d66c0f-…`, not the date-only `DatePicker` the rest of
the site uses), `durationMinutes`, `capacity`, `instructor`, `location`. No template: a class is
data the portal renders, not a page. `capacity` is the club's "max x players" and shows up on the
portal as "8 av 8 platser kvar".

### Decisions

- **Zero capacity means not bookable, never unlimited.** A field an editor never filled in reads as
  zero, and the safe direction to fail is turning people away rather than overfilling a court.
- **`BookingSnapshot` gained `ClassKey`.** Without it a booking could not find its class, and the
  portal row would have had to render a placeholder. Caught while writing the controller — the
  first draft had `FindClassFor` returning null and `UsedCredit` hardcoded false, which is the kind
  of placeholder that survives into production.
- **A booking whose class has been deleted still renders**, using the start time the booking carries
  itself. Someone who paid deserves to see their booking whatever the editor has since done.
- **One query for every class on the page**, not one per class, so a portal listing twenty classes
  does not issue twenty round trips.
- **The reminder banner is a pure read of the bookings list**, so the banner and the list cannot
  disagree about what is coming up.
- **Access is Umbraco's public access, not a controller check.** The pipeline redirects an anonymous
  visitor before any of our code runs, so no forgotten guard can expose member content. The
  controller's null check on the current member is belt and braces.

### API findings

- **`IMemberManager` has no role methods** (`AddToRoleAsync`, `IsInRoleAsync`). Group assignment
  goes through `IMemberService.AssignRole(username, roleName)`, inherited from
  `IMembershipRoleService<IMember>`. Without it a verified member could sign in and then be bounced
  straight off their own portal, because group membership is what public access actually checks.
- `IMemberGroupService.CreateAsync` takes **one** argument and returns
  `Attempt<IMemberGroup?, MemberGroupOperationStatus>`.
- **A Razor local variable may not be called `model`.** `@model.Foo` is parsed as the reserved
  `@model` directive and fails the build with RZ2005. Renamed to `portal`.
- **`@(` followed by a newline** also fails: the expression must start on the same line.

### Runtime verification

| Check | Result |
| --- | --- |
| Installer | portal page, `Träningar` folder, `Medlemmar` group, access rule, three example classes — all created, no errors |
| Anonymous request to `/mina-sidor/` | renders the **login page**; zero portal content in the response |
| Full journey: register → verify → login | lands on `/mina-sidor/` |
| Capacity display | 8 av 8, 6 av 6, 4 av 4 — matching the seeded capacities |
| First-class pricing | "Nästa träning kostar **100 kr** (välkomstpris)" |
| Membership status | "Årsavgiften på 150 kr läggs till när du bokar din nästa träning" |
| Time zone round-trip | seeded 18:00 Swedish → stored UTC → rendered "onsdag 26 augusti 18:00" |
| Sidebar when signed in | "Mina sidor" + "Logga ut" |

**Booking buttons render disabled**, labelled with the real price. Phase 4 wires them up; showing
the final layout now means Phase 4 changes behaviour rather than appearance.

---

## Tasks 15–18: Booking and the mocked Swish payment — Phase 4

**Files created:** `Booking/Payments/IPaymentProvider.cs`, `SwishMockPaymentProvider.cs`;
`Booking/Services/BookingService.cs`; `Booking/Web/BookingSurfaceController.cs`,
`SwishPaymentController.cs`, `SwishPaymentSurfaceController.cs`; `Views/SwishPayment.cshtml`.
**Files modified:** `IBookingRepository`/`BookingRepository` (write side), keys, installer
(`swishPayment` type + template), `NdstkMemberPages` (payment page), `MemberPortalViewModel`,
`Views/MemberPortal.cshtml`, `BookingComposer`, `site.css`.

### Overbooking: the one thing worth engineering carefully

Reserving a place is **a single conditional INSERT**, not a count followed by an insert:

```sql
INSERT INTO ndstkBooking (...) SELECT @0, @1, ...
WHERE (SELECT COUNT(*) FROM ndstkBooking
       WHERE ClassKey = @1
         AND (Status = 'Confirmed' OR (Status = 'Pending' AND HoldExpiresUtc > @4))) < @7
```

Two statements would leave a window, however short, in which two members both read "one place
left". `ICoreScope.WriteLock` was the alternative, but it needs rows added to Umbraco's `umbracoLock`
table; one atomic statement needs nothing and is easier to reason about.

**Measured, not assumed.** A harness fired concurrent reservations at a fresh class:

| Attempts | Capacity | Rows inserted | Result |
| --- | --- | --- | --- |
| 12 | 4 | 4 | never exceeded |
| 60 | 8 | 8 | never exceeded |

**Caveat worth recording:** SQLite serialises writers, which helps this hold. The same statement on
SQL Server under READ COMMITTED would want a lock hint. This site is SQLite — the connection string
in `appsettings.json` confirms it — so the guarantee is real here, but the note matters if the
database is ever moved.

The same technique protects credits: `TrySpendCreditAsync` puts `SpentOnBookingId IS NULL` in the
UPDATE's WHERE clause, so two bookings racing for one credit cannot both win.

### Decisions

- **The credit is chosen before the place is reserved but spent after**, so a member who asked to
  use a credit and then found the class full has not lost it. If the credit is taken in between, the
  reservation is released rather than silently charging them instead — they asked to use a credit.
- **The welcome price is consumed by reading the member's own flag, not by comparing amounts.**
  Comparing the stored `ClassFeeOre` against the configured price would break the moment an editor
  changed prices between a booking and its payment, and would misfire entirely if the two prices
  were ever set the same. `Pricing` only quotes the welcome price while the flag is false, so a
  class fee charged to a member whose flag is false *was* the welcome price.
- **The booking button shows the total, membership fee included.** Quoting the class fee alone and
  then presenting a larger figure on the payment page would read as a bait and switch.
- **Settling checks the payment is still `Pending`.** Without it, a repeated POST would extend a
  membership twice.
- **The payment page is a child of the portal**, so it inherits the portal's public access — an
  anonymous visitor cannot reach it even holding a valid reference.
- **Two separate booking forms** (pay / use a credit) rather than one with a toggle, so each button
  posts exactly what its label says.
- The page is plainly marked **"Demoläge"** with a deliberately fake QR block. Nobody should mistake
  it for a real payment.

### Runtime verification

| Check | Result |
| --- | --- |
| Booking button for a brand-new member | **250 kr** (150 membership + 100 welcome) |
| Book → redirect | `/mina-sidor/betalning/?ref=…` |
| Payment page | "Demoläge", 250 kr, split as Årsavgift 150 + Träningsavgift 100 |
| Settle → membership | extended to **2027-08-24** (today + 365) |
| Settle → welcome price | consumed; next class now quotes **200 kr** |
| Settle → booking | appears under "Mina bokningar" |
| Pending hold counts toward capacity | class showed **6 av 8** with one confirmed and one pending |
| **Non-owner views a reference** | "Betalningen hittades inte", no amount, no buttons |
| **Non-owner POSTs a reference with genuinely valid tokens from their own payment page** | **404**, and the owner's payment left untouched |

That last row is the one that matters: the attacker had real antiforgery and `ufprt` tokens, so the
404 came from the ownership check rather than from antiforgery.

**The abort path, verified after waiting out the rate limiter:**

| Check | Result |
| --- | --- |
| "Simulera avbrott" POST | 302 to the portal, "Betalningen avbröts, så platsen är inte bokad" |
| Place released | class went from **6 av 8** back to **7 av 8** |
| Revisiting the reference | "Betalningen är avslutad" — cannot be settled afterwards |
| The aborted booking | absent from the member's bookings; `Expired` is filtered out of the list |
| The other member's confirmed booking | untouched |

**A testing note worth keeping.** Two of these tests initially failed because of the *test*, not the
code. `tail -1` on the payment page's hidden fields picks up the **logout** form — the layout's
sidebar renders after the body, so it is the last form in the document — which signed the member out
instead of aborting the payment. Target the form by the block containing its button text. And the
rate limiter locked out the test run twice, which is inconvenient but exactly the behaviour wanted.

---

## Tasks 19–20: Cancellation, credits and rebooking — DONE (Phase 5 complete)

**Files modified:** `IBookingRepository`/`BookingRepository` (`TryCancelBookingAsync`),
`BookingService` (`CancelAsync`), `BookingSurfaceController` (`Cancel`),
`Views/MemberPortal.cshtml` (cancel button), `site.css`.

### Every precondition lives in the UPDATE

```sql
UPDATE ndstkBooking SET Status = 'Cancelled', CancelledUtc = @1, HoldExpiresUtc = NULL
WHERE Id = @2 AND MemberKey = @3 AND Status = 'Confirmed' AND ClassStartUtc > @1
```

One statement does four jobs: it stops a member cancelling somebody else's booking, stops a class
being cancelled after it has started, stops a double submission minting a second credit, and means
the credit row is only ever inserted by the caller that actually performed the cancellation. Checking
those conditions in C# first and then updating would reopen the gap.

### Decisions

- **A booking paid for with a credit still yields one back on cancellation.** Otherwise cancelling
  would cost the member the credit they came in with. Net zero either way, so the club loses nothing.
- **The no-refund rule is stated on the button itself**, as its title attribute, and repeated in the
  confirmation. A member should not discover it afterwards.
- **One message for every refusal** — not yours, not confirmed, already started. Distinguishing them
  would tell a member whether a booking id they guessed exists.
- **Spending a credit when the membership is valid skips Swish entirely**, because the total is zero.
  That path was already built in Phase 4; this phase is what finally exercises it.

### Runtime verification

| Check | Result |
| --- | --- |
| Cancel a confirmed future booking | 302, "Avgiften betalas inte tillbaka, men du har fått en tillgodoträning" |
| Place released | **7 av 8 → 8 av 8** |
| Booking row | tagged "Avbokad" |
| Credit issued | "Boka med tillgodoträning" button appeared |
| Book with the credit | no Swish step at all; "Klart! Din träning är bokad med en tillgodoträning" |
| New booking | tagged "Tillgodoträning"; places **8 av 8 → 7 av 8** |
| Credit consumed | the unspent-credits notice disappeared |
| **Replay the same cancellation** | "Den bokningen kan inte avbokas" — **no second credit minted** |
| **Cancel another member's booking id** | identical message, no credit minted |

---

## Tasks 21–23: Reminders, the sweeper and editor changes — Phase 6

**Added to scope by the user mid-phase:** behaviour for an expired membership — re-apply the
membership fee plus the class fee, but **without** the welcome discount.

### The expiry rule already worked; it just was not pinned

A consequence of the "once ever, per account" choice for the welcome price: `firstClassDiscountUsed`
never resets, so a lapsed member renewing pays 150 + 200 = 350, not 150 + 100. Four tests now hold
that in place, including the one case where a lapsed member *does* still get the welcome price —
they never used it, so someone who registered, never booked, and let a comped membership lapse is
still a first-timer.

What was genuinely missing was the member-facing half: the portal told a lapsed member the same
thing as a brand-new one. `MembershipStatus` now distinguishes `IsNew` from `HasLapsed`, and a
lapsed member is told the date it ran out and that it renews on their next booking. Telling someone
their membership "will be added" when it demonstrably *lapsed* on a date they can check reads as a
bug in the club's system.

**Files:** `NDSTK.Tests/PricingTests.cs` (+4), `Booking/Web/MemberPortalViewModel.cs`,
`Views/MemberPortal.cshtml`.

### The reminder job

`ClassReminderJob : IRecurringBackgroundJob`, 15-minute period, 2-minute start delay.

- **Guarded by `IServerRoleAccessor`** — only `Single` or `SchedulingPublisher` runs it. Without
  that, every server in a multi-server deployment would send every member the same reminder.
- **Resolves its dependencies from a fresh `IServiceScope` per run.** The job is a singleton, so
  injecting the scoped services directly would capture them for the process lifetime.
- **Stamps before sending, conditionally.** `TryStampReminderSentAsync` sets `ReminderSentUtc` only
  where it is still null, so two overlapping runs cannot both send; the loser skips. The trade is
  that a crash between stamp and send loses that one reminder — preferable to mailing a member the
  same reminder repeatedly.
- **Sweeps expired holds first**, so a place released this pass is already free within it.

### Editor changes

`TrainingClassChangedHandler` on `ContentPublished`, `ContentUnpublished` and `ContentDeleted`.

- **Moving a class** repoints every live booking at the new start time, and clears
  `ReminderSentUtc` **only when the class moved later** — having been told "imorgon 18:00" for a
  class that has since moved is worse than not having been told. Cancelled bookings are left alone:
  they are a record of the class as it was, and rewriting their time would falsify it.
- **Unpublishing or deleting** a class cancels its live bookings and issues a credit for each
  **confirmed** one. A pending booking was never paid for, so crediting it would be free money.

### A datetime-format trap, found while testing

The app stores `DateTime` as TEXT in NPoco's `yyyy-MM-dd HH:mm:ss.fffffff`. My first test tool wrote
round-trip `"o"` format (`…T…Z`) into the same column, and the reminder silently matched nothing:
comparing `2026-08-25T15:36…` against a window end of `2026-08-25 18:26…` puts `T` (84) above space
(32), pushing the row *outside* the window.

**Production is not affected** — every insert and every query parameter goes through the same NPoco
converter, so the column is internally consistent, and that fixed-width zero-padded format sorts
lexicographically exactly as it sorts chronologically (a value with no fractional part is a prefix
of one with, so it also compares earlier, which is correct). Worth recording because any future raw
SQL that formats a date by hand would reintroduce it.

### Runtime verification

| Check | Result |
| --- | --- |
| Job registration | "ClassReminderJob with a delay of 00:02:00, running every 00:15:00" |
| Class-move resync | live rows follow the new time and lose their reminder stamp; the cancelled row keeps its original time |
| Portal reminder banner, real data | "Påminnelse: Nybörjartennis börjar tisdag 25 augusti 17:38. Plats: Bana 1." |
| Membership copy, valid | "Årsavgiften är betald till och med 2027-08-24" |

| Reminder actually sent | "Sent 1 class reminder(s)"; `.eml` from `info@ndstk.se`, subject "Påminnelse: Nybörjartennis hos NDSTK imorgon", body "tisdag 25 augusti kl. 17:38 / Plats: Bana 1" |
| **No resend on the next run** | "0 booking(s) due a reminder", mail count unchanged |
| **Hold sweeper** | "Released 1 abandoned payment hold(s)"; booking → `Expired`, hold cleared |
| **Credit returned by the sweep** | the spent credit's `SpentOnBookingId` went back to NULL |

The "moved earlier keeps the stamp" branch is correct by inspection of the `CASE` expression but was
not isolated in the run above, because the preceding step had already cleared the stamp.

**Observability change worth keeping.** The job originally logged only when it found something,
which makes "ran and found nothing" indistinguishable from "never ran" — that cost real time
diagnosing the first silent run. It now logs the server role and the due count at Debug, and
`appsettings.Development.json` overrides the `NDSTK` namespace to Debug so local runs are visible
without framework noise.

---

## Remaining tasks

Task 24 (Phase 7) is written into this document as it is reached, so that the interfaces they
consume are the ones the previous task actually produced rather than the ones this plan
predicted. Phase boundaries are fixed by the spec:

| Phase | Tasks | Scope |
| --- | --- | --- |
| 2 | 6–11 | Installer upgrade capabilities, Settings fields and member properties, profile service, mail, registration, verification, login and logout, all form security |
| 3 | 12–14 | `trainingClass` document type, class service, member portal page with live capacity |
| 4 | 15–18 | Booking repository, `IPaymentProvider`, booking service, Swish mock page, confirm and fail |
| 5 | 19–20 | Cancellation, credit issue, rebooking with a credit |
| 6 | 21–23 | Reminder job, reminder mail, portal banner, hold sweeper, editor-change handlers |
| 7 | 24 | Repoint the calls to action, seed content, styling, notes |
