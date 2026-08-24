# Member Administration and Family Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a booking belong to a named child rather than to an account, sell a family account that puts several children on one login, and give the club a backoffice view of members, payments and class rosters.

**Architecture:** A new `ndstkParticipant` table holds the people who attend; `ndstkBooking` gains a `ParticipantKey` and its partial unique index moves from `(MemberKey, ClassKey)` to `(ParticipantKey, ClassKey)`, which is what lets two siblings share a class. The membership clock stays on the Umbraco member — one expiry date per account — with a `familjekonto` flag deciding whether renewal costs one fee or two. The backoffice is a plain ES-module Lit element in `wwwroot/App_Plugins/` talking to a Management API controller; no npm and no bundler.

**Tech Stack:** Umbraco CMS 18.1.1, .NET 10, SQLite, NPoco, xUnit, Lit (from the backoffice import map).

**Spec:** `docs/superpowers/specs/2026-08-25-member-administration-design.md`

## Global Constraints

- **Umbraco 18.1.1 on .NET 10, SQLite.** Migrations derive from `AsyncMigrationBase` and override `MigrateAsync()`. `MigrationBase` does not exist in v18.
- **Money is integer öre everywhere.** The ×100 from kronor happens exactly once, in `MembershipSettingsService`. Never store a decimal — SQLite maps `decimal` to `REAL`.
- **A zero setting counts as "not set", per field**, and falls back to its own default independently.
- **`NDSTK.Domain` must never reference Umbraco or a database.** Pure functions only. This is enforced by the project reference graph; do not add a package reference to it.
- **All member-facing copy is Swedish.** Backoffice copy is Swedish too, matching the existing `Medlemskap` / `Träningar` labels.
- **Birth dates are entered and displayed as `ÅÅÅÅMMDD`** (eight digits) and stored as a date. **No personnummer is collected or stored** — eight digits and stop.
- **Never hand-format a date into raw SQL.** Pass it as an NPoco parameter. NPoco writes `yyyy-MM-dd HH:mm:ss.fffffff`; round-tripped `"o"` format sorts differently as text and silently breaks range queries.
- **No npm, no bundler, no build step** for the backoffice. Hand-written `umbraco-package.json` + plain `.js`, following `c:\src\Esatto.Packages\Esatto.Umbraco.Backoffice.Redirects`.
- **Build while the site is running:** `dotnet build -t:CoreCompile` (a running site holds a lock on the output). Tests: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`.
- **Commit after every task.** Do not batch.

## File Structure

**Created**

| File | Responsibility |
| --- | --- |
| `NDSTK.Domain/ParticipantState.cs` | The one pricing-relevant fact about a child |
| `Booking/Data/ParticipantRecord.cs` | NPoco POCO for `ndstkParticipant` |
| `Booking/Data/IParticipantRepository.cs` | Read/write surface for participants |
| `Booking/Data/ParticipantRepository.cs` | Its NPoco implementation |
| `Booking/Data/Migrations/AddParticipantTable.cs` | Schema only: table + two columns |
| `Booking/Services/NdstkParticipantBackfill.cs` | Data backfill + the index swap, guarded by a key/value marker |
| `Booking/Services/ParticipantService.cs` | Add/edit/remove a child, with the family-account rule |
| `Booking/Web/ParticipantFormModel.cs` | The child form, including `ÅÅÅÅMMDD` parsing |
| `Booking/Web/ParticipantSurfaceController.cs` | Mina barn POST endpoints |
| `Booking/Web/FamilyUpgradeSurfaceController.cs` | Buying the family account |
| `Booking/Admin/MemberAdminRow.cs` | One dashboard row |
| `Booking/Admin/MemberAdminDetail.cs` | Payments + bookings for one account |
| `Booking/Admin/ClassRosterRow.cs` | One line of a class roster |
| `Booking/Admin/MemberAdminQueries.cs` | All reporting SQL. Read-only, kept out of `IBookingRepository` |
| `Booking/Admin/MemberAdminController.cs` | Management API, gated on `SectionAccessMembers` |
| `Views/Partials/MemberChildren.cshtml` | Mina barn |
| `wwwroot/App_Plugins/NDSTK.MemberAdmin/umbraco-package.json` | Both extension manifests |
| `wwwroot/App_Plugins/NDSTK.MemberAdmin/members-dashboard.js` | Medlemmar dashboard |
| `wwwroot/App_Plugins/NDSTK.MemberAdmin/class-roster.js` | Deltagare workspace view |
| `NDSTK.Tests/ParticipantPricingTests.cs` | Family + per-child pricing |

**Modified**

| File | Change |
| --- | --- |
| `NDSTK.Domain/PriceList.cs` | `+ FamilyFeeOre` |
| `NDSTK.Domain/MemberState.cs` | `FirstClassDiscountUsed` → `IsFamilyAccount` |
| `NDSTK.Domain/BookingQuote.cs` | `+ FamilyDueOre` |
| `NDSTK.Domain/Pricing.cs` | Takes a `ParticipantState`; family fee on renewal |
| `NDSTK.Domain/BookingSnapshot.cs` | `+ ParticipantKey` |
| `NDSTK.Domain/Capacity.cs` | `HasLiveBooking` keyed by participant |
| `Booking/Data/BookingRecord.cs` | `+ ParticipantKey` |
| `Booking/Data/PaymentRecord.cs` | `+ FamilyFeeOre` |
| `Booking/Data/BookingTables.cs` | `+ Participant` |
| `Booking/Data/Migrations/BookingMigrationPlan.cs` | New migration step |
| `Booking/Data/BookingRepository.cs` | Participant on insert, select and snapshot |
| `Booking/Services/BookingService.cs` | Books for a participant |
| `Booking/Services/MemberProfileService.cs` | `familjekonto`; trial flag moves to the participant |
| `Booking/Services/MembershipSettingsService.cs` | Reads `familyFee` |
| `Booking/Services/MembershipSettings.cs` | `+ FamilyFeeOre` default |
| `Booking/BookingComposer.cs` | Registers the new services |
| `Booking/Web/RegisterFormModel.cs` | Six new required fields |
| `Booking/Web/RegisterSurfaceController.cs` | Writes the first participant |
| `Booking/Web/BookingSurfaceController.cs` | Accepts a participant key |
| `Booking/Web/MemberPortalViewModel.cs` | Carries participants |
| `Booking/Web/MemberPortalController.cs` | Loads them |
| `ContentModel/NdstkContentModelInstaller.cs` | `familjekonto`, `telefon`, `familyFee` |
| `Views/MemberRegister.cshtml` | The new fields |
| `Views/MemberPortal.cshtml` | Mina barn + the child picker |
| `NDSTK.Tests/PricingTests.cs` | Updated signatures |
| `NDSTK.Tests/CapacityTests.cs` | Updated signatures |
| `README.md` | Participants, family accounts, the backoffice |

---

## Phase 8 — Data model and rules

### Task 1: Pricing with a family account and a per-child welcome price

**Files:**
- Create: `NDSTK.Domain/ParticipantState.cs`
- Modify: `NDSTK.Domain/PriceList.cs`, `NDSTK.Domain/MemberState.cs`, `NDSTK.Domain/BookingQuote.cs`, `NDSTK.Domain/Pricing.cs`
- Test: `NDSTK.Tests/ParticipantPricingTests.cs`, `NDSTK.Tests/PricingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Pricing.Quote(MemberState, ParticipantState, PriceList, bool useCredit, DateOnly today) → BookingQuote`; `Pricing.FamilyUpgradeQuote(PriceList) → BookingQuote`; `MemberState(DateOnly? MembershipPaidUntil, bool IsFamilyAccount)`; `ParticipantState(bool FirstClassUsed)`; `PriceList(int MembershipFeeOre, int FamilyFeeOre, int FirstClassPriceOre, int ClassPriceOre)`; `BookingQuote(int MembershipDueOre, int FamilyDueOre, int ClassFeeOre)` with `TotalOre` and `RequiresPayment`.

- [ ] **Step 1: Write the failing tests**

Create `NDSTK.Tests/ParticipantPricingTests.cs`:

```csharp
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class ParticipantPricingTests
{
    private static readonly PriceList Prices = new(
        MembershipFeeOre: 15_000,
        FamilyFeeOre: 10_000,
        FirstClassPriceOre: 10_000,
        ClassPriceOre: 20_000);

    private static readonly DateOnly Today = new(2026, 8, 25);

    private static MemberState Solo(DateOnly? paidUntil) => new(paidUntil, IsFamilyAccount: false);
    private static MemberState Family(DateOnly? paidUntil) => new(paidUntil, IsFamilyAccount: true);
    private static ParticipantState NewChild => new(FirstClassUsed: false);
    private static ParticipantState OldChild => new(FirstClassUsed: true);

    [Fact]
    public void Lapsed_family_account_pays_the_membership_fee_and_the_family_supplement()
    {
        BookingQuote quote = Pricing.Quote(Family(null), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
        Assert.Equal(45_000, quote.TotalOre);
    }

    [Fact]
    public void Lapsed_solo_account_is_not_charged_the_family_supplement()
    {
        BookingQuote quote = Pricing.Quote(Solo(null), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
    }

    [Fact]
    public void Paid_up_family_account_pays_neither_fee_again()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void The_welcome_price_is_per_child_not_per_account()
    {
        MemberState paidUp = Family(new DateOnly(2027, 1, 1));

        BookingQuote first = Pricing.Quote(paidUp, NewChild, Prices, useCredit: false, Today);
        BookingQuote second = Pricing.Quote(paidUp, NewChild, Prices, useCredit: false, Today);

        // Two different children, each on their first class: both get the welcome price.
        Assert.Equal(10_000, first.ClassFeeOre);
        Assert.Equal(10_000, second.ClassFeeOre);
    }

    [Fact]
    public void A_child_who_has_used_their_welcome_price_pays_full_price()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(20_000, quote.ClassFeeOre);
    }

    [Fact]
    public void A_credit_clears_the_class_fee_but_never_the_membership_or_family_fee()
    {
        BookingQuote quote = Pricing.Quote(Family(null), NewChild, Prices, useCredit: true, Today);

        Assert.Equal(0, quote.ClassFeeOre);
        Assert.Equal(15_000, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.True(quote.RequiresPayment);
    }

    [Fact]
    public void A_paid_up_member_spending_a_credit_owes_nothing_at_all()
    {
        BookingQuote quote = Pricing.Quote(
            Family(new DateOnly(2027, 1, 1)), NewChild, Prices, useCredit: true, Today);

        Assert.Equal(0, quote.TotalOre);
        Assert.False(quote.RequiresPayment);
    }

    [Fact]
    public void The_family_upgrade_is_quoted_on_its_own_with_no_class_or_membership_fee()
    {
        BookingQuote quote = Pricing.FamilyUpgradeQuote(Prices);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(10_000, quote.FamilyDueOre);
        Assert.Equal(0, quote.ClassFeeOre);
        Assert.Equal(10_000, quote.TotalOre);
    }

    [Fact]
    public void Membership_expiring_today_is_still_valid_for_a_family_account()
    {
        BookingQuote quote = Pricing.Quote(Family(Today), OldChild, Prices, useCredit: false, Today);

        Assert.Equal(0, quote.MembershipDueOre);
        Assert.Equal(0, quote.FamilyDueOre);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: FAIL — `PriceList` has no `FamilyFeeOre`, `MemberState` has no `IsFamilyAccount`, `Pricing.Quote` has the wrong arity.

- [ ] **Step 3: Add `ParticipantState`**

Create `NDSTK.Domain/ParticipantState.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The one fact about a child that affects what a booking costs.
/// </summary>
/// <remarks>
/// Separate from <see cref="MemberState"/> because the welcome price is per child and the
/// membership is per account. Keeping them apart is what stops a second child on a family account
/// silently inheriting their sibling's spent discount.
/// </remarks>
/// <param name="FirstClassUsed">True once a payment that charged this child the welcome price completed.</param>
public sealed record ParticipantState(bool FirstClassUsed);
```

- [ ] **Step 4: Widen `PriceList`, `MemberState` and `BookingQuote`**

`NDSTK.Domain/PriceList.cs` — add `FamilyFeeOre` **second**, so a positional construction that forgets it fails to compile rather than silently shifting the class price into the membership slot:

```csharp
public sealed record PriceList(
    int MembershipFeeOre, int FamilyFeeOre, int FirstClassPriceOre, int ClassPriceOre);
```

`NDSTK.Domain/MemberState.cs` — replace the record entirely:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The two facts about an account that affect what a booking costs. Both are stored as member type
/// properties so an administrator can comp a membership, or grant a family account, from the
/// backoffice without touching SQL.
/// </summary>
/// <param name="MembershipPaidUntil">Inclusive last day of the paid membership; null when never paid.</param>
/// <param name="IsFamilyAccount">
/// True when the account may hold more than one participant. It costs a supplement on top of the
/// annual fee, charged with it, so renewal is one fee or two depending on this flag alone.
/// </param>
public sealed record MemberState(DateOnly? MembershipPaidUntil, bool IsFamilyAccount);
```

`NDSTK.Domain/BookingQuote.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// What one booking costs, split three ways so the payment page and the backoffice can both show
/// the member exactly what they are paying for.
/// </summary>
public sealed record BookingQuote(int MembershipDueOre, int FamilyDueOre, int ClassFeeOre)
{
    public int TotalOre => MembershipDueOre + FamilyDueOre + ClassFeeOre;

    /// <summary>False when the total is zero, in which case the Swish step is skipped entirely.</summary>
    public bool RequiresPayment => TotalOre > 0;
}
```

- [ ] **Step 5: Rewrite `Pricing`**

`NDSTK.Domain/Pricing.cs`:

```csharp
namespace NDSTK.Booking.Domain;

/// <summary>
/// The whole pricing rule, as a pure function. Deliberately free of Umbraco and the database so
/// every combination of membership, family account, discount and credit is cheap to test.
/// </summary>
public static class Pricing
{
    public static BookingQuote Quote(
        MemberState member, ParticipantState participant, PriceList prices,
        bool useCredit, DateOnly today)
    {
        var valid = IsMembershipValid(member, today);

        // The family supplement rides along with the annual fee and is never charged on its own
        // here. Buying it mid-year is a separate purchase - see FamilyUpgradeQuote - which
        // deliberately does not move the expiry date.
        int membershipDueOre = valid ? 0 : prices.MembershipFeeOre;
        int familyDueOre = valid || member.IsFamilyAccount is false ? 0 : prices.FamilyFeeOre;

        // A credit is worth one place, so it clears the class fee but never the membership or
        // family fee. It also leaves the welcome price unspent - see FirstClassUsed, which only
        // moves when a payment that actually charged it completes.
        int classFeeOre = useCredit
            ? 0
            : participant.FirstClassUsed
                ? prices.ClassPriceOre
                : prices.FirstClassPriceOre;

        return new BookingQuote(membershipDueOre, familyDueOre, classFeeOre);
    }

    /// <summary>
    /// Upgrading a paid-up account to a family account, mid-year, as a purchase of its own.
    /// </summary>
    /// <remarks>
    /// Deliberately does not extend the membership. If it did, the supplement would be cheaper than
    /// the annual fee and no member would ever pay the annual fee a second time. The trade is that
    /// upgrading a month before expiry buys only that month, which is visible to the member at the
    /// time and self-correcting: they renew at the family price next time.
    /// </remarks>
    public static BookingQuote FamilyUpgradeQuote(PriceList prices)
        => new(MembershipDueOre: 0, FamilyDueOre: prices.FamilyFeeOre, ClassFeeOre: 0);

    /// <summary>The paid-until day is inclusive: a membership expiring today is still valid today.</summary>
    public static bool IsMembershipValid(MemberState member, DateOnly today)
        => member.MembershipPaidUntil is { } paidUntil && paidUntil >= today;
}
```

- [ ] **Step 6: Update the existing `PricingTests`**

Every call in `NDSTK.Tests/PricingTests.cs` needs the new arity. Change the fixtures at the top:

```csharp
    private static readonly PriceList Prices = new(
        MembershipFeeOre: 15_000,
        FamilyFeeOre: 10_000,
        FirstClassPriceOre: 10_000,
        ClassPriceOre: 20_000);

    private static MemberState Member(DateOnly? paidUntil, bool discountUsed = false)
        => new(paidUntil, IsFamilyAccount: false);

    private static ParticipantState Child(bool discountUsed) => new(discountUsed);
```

then update each `Pricing.Quote(Member(...), Prices, ...)` call to `Pricing.Quote(Member(...), Child(<the discountUsed value from that test>), Prices, ...)`. The assertions do not change: a solo account's quote is identical to the old behaviour, which is the point.

- [ ] **Step 7: Run the tests**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, all previous tests plus the nine new ones.

- [ ] **Step 8: Commit**

```bash
git add NDSTK.Domain NDSTK.Tests
git commit -m "Price a booking per child, with a family supplement on renewal"
```

---

### Task 2: Capacity and the one-live-booking rule, keyed by participant

**Files:**
- Modify: `NDSTK.Domain/BookingSnapshot.cs`, `NDSTK.Domain/Capacity.cs`
- Test: `NDSTK.Tests/CapacityTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `BookingSnapshot(int Id, Guid MemberKey, Guid ParticipantKey, Guid ClassKey, string Status, DateTime? HoldExpiresUtc, DateTime ClassStartUtc, DateTime? ReminderSentUtc)`; `Capacity.HasLiveBooking(IEnumerable<BookingSnapshot>, Guid participantKey, DateTime nowUtc)`.

- [ ] **Step 1: Write the failing tests**

Append to `NDSTK.Tests/CapacityTests.cs`:

```csharp
    [Fact]
    public void Two_siblings_may_both_hold_a_live_booking_on_the_same_class()
    {
        Guid elsa = Guid.NewGuid();
        Guid nils = Guid.NewGuid();
        Guid guardian = Guid.NewGuid();
        Guid classKey = Guid.NewGuid();
        DateTime now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        BookingSnapshot[] bookings =
        [
            new(1, guardian, elsa, classKey, BookingStatus.Confirmed, null, now.AddDays(1), null),
            new(2, guardian, nils, classKey, BookingStatus.Confirmed, null, now.AddDays(1), null),
        ];

        // Both children are booked, and each is individually blocked from booking again -
        // but neither blocks the other, which is the whole point of a family account.
        Assert.True(Capacity.HasLiveBooking(bookings, elsa, now));
        Assert.True(Capacity.HasLiveBooking(bookings, nils, now));
        Assert.Equal(6, Capacity.RemainingPlaces(8, bookings, now));
    }

    [Fact]
    public void A_sibling_booking_does_not_make_another_child_look_booked()
    {
        Guid elsa = Guid.NewGuid();
        Guid vera = Guid.NewGuid();
        Guid guardian = Guid.NewGuid();
        Guid classKey = Guid.NewGuid();
        DateTime now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        BookingSnapshot[] bookings =
        [
            new(1, guardian, elsa, classKey, BookingStatus.Confirmed, null, now.AddDays(1), null),
        ];

        Assert.False(Capacity.HasLiveBooking(bookings, vera, now));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CapacityTests`
Expected: FAIL — `BookingSnapshot` takes seven arguments, not eight.

- [ ] **Step 3: Add `ParticipantKey` to `BookingSnapshot`**

In `NDSTK.Domain/BookingSnapshot.cs`, insert the parameter after `MemberKey` and document why both are kept:

```csharp
public sealed record BookingSnapshot(
    int Id,
    Guid MemberKey,
    Guid ParticipantKey,
    Guid ClassKey,
    string Status,
    DateTime? HoldExpiresUtc,
    DateTime ClassStartUtc,
    DateTime? ReminderSentUtc);
```

Add to the doc comment above it:

```csharp
/// <param name="MemberKey">
/// The account that pays. Kept alongside <paramref name="ParticipantKey"/> rather than reached
/// through a join, because every payment, credit and reminder query keys off it.
/// </param>
/// <param name="ParticipantKey">The child who attends. What the capacity and duplicate rules use.</param>
```

- [ ] **Step 4: Key `HasLiveBooking` on the participant**

In `NDSTK.Domain/Capacity.cs`:

```csharp
    /// <summary>
    /// A <em>child</em> may hold at most one live booking per class. A cancelled or expired booking
    /// does not count, so rebooking a class you left is allowed.
    /// </summary>
    /// <remarks>
    /// Keyed on the participant, not the account: two siblings on one family account are two
    /// participants, and both must fit on the same class. This must stay in step with the partial
    /// unique index IX_ndstkBooking_OneLivePerParticipantClass, which is the same rule in SQL.
    /// </remarks>
    public static bool HasLiveBooking(
        IEnumerable<BookingSnapshot> bookings, Guid participantKey, DateTime nowUtc)
        => bookings.Any(booking =>
            booking.ParticipantKey == participantKey && HoldsPlace(booking, nowUtc));
```

- [ ] **Step 5: Fix the existing `CapacityTests` constructions**

Every existing `new BookingSnapshot(...)` in the file gains a participant key after the member key. Where a test only cares about capacity, pass `Guid.NewGuid()`. Where a test asserts `HasLiveBooking`, give the snapshot and the assertion the **same** guid.

- [ ] **Step 6: Run the tests**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add NDSTK.Domain NDSTK.Tests
git commit -m "Key the one-live-booking rule on the child, not the account"
```

---

### Task 3: The participant table and its schema migration

**Files:**
- Create: `Booking/Data/ParticipantRecord.cs`, `Booking/Data/Migrations/AddParticipantTable.cs`
- Modify: `Booking/Data/BookingTables.cs`, `Booking/Data/BookingRecord.cs`, `Booking/Data/PaymentRecord.cs`, `Booking/Data/Migrations/BookingMigrationPlan.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: table `ndstkParticipant`; `BookingRecord.ParticipantKey` (`Guid?`); `PaymentRecord.FamilyFeeOre` (`int`).

- [ ] **Step 1: Add the table name**

`Booking/Data/BookingTables.cs`:

```csharp
    internal const string Participant = "ndstkParticipant";
```

- [ ] **Step 2: Create the POCO**

Create `Booking/Data/ParticipantRecord.cs`:

```csharp
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace NDSTK.Booking.Data;

/// <summary>
/// One person who attends classes. The account holder is a guardian; this is the child.
/// </summary>
/// <remarks>
/// A table rather than an Umbraco member: Umbraco requires a unique email per member, so three
/// siblings would mean three synthesised addresses and three Identity logins to disable. A table
/// rather than content nodes: these are minors' names and birth dates, and they have no business
/// in the published cache.
/// </remarks>
[TableName(BookingTables.Participant)]
[PrimaryKey(nameof(Id))]
[ExplicitColumns]
public sealed class ParticipantRecord
{
    [Column(nameof(Id))]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    /// <summary>What bookings reference. A key rather than the id so it is safe to put in a form.</summary>
    [Column(nameof(Key))]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_ndstkParticipant_Key")]
    public Guid Key { get; set; }

    /// <summary>The guardian's account.</summary>
    [Column(nameof(MemberKey))]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkParticipant_MemberKey")]
    public Guid MemberKey { get; set; }

    [Column(nameof(FirstName))]
    [Length(100)]
    public string FirstName { get; set; } = string.Empty;

    [Column(nameof(LastName))]
    [Length(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Null only on rows the backfill created for members who registered before participants
    /// existed. The portal refuses to book for such a child until it is filled in - inventing a
    /// birth date would be worse than asking.
    /// </summary>
    [Column(nameof(BirthDate))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// The welcome price, per child. Moved off the member so a second child on a family account
    /// does not inherit their sibling's spent discount.
    /// </summary>
    [Column(nameof(FirstClassUsedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? FirstClassUsedUtc { get; set; }

    [Column(nameof(CreatedUtc))]
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Soft delete. Removing the row outright would orphan the child's bookings and quietly change
    /// last season's class numbers.
    /// </summary>
    [Column(nameof(RemovedUtc))]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? RemovedUtc { get; set; }
}
```

- [ ] **Step 3: Add the two new columns to the existing POCOs**

`Booking/Data/BookingRecord.cs`, after `MemberKey`:

```csharp
    /// <summary>
    /// The child this place is for. Nullable only so the migration can add the column before the
    /// backfill fills it; every row written after the backfill has one.
    /// </summary>
    [Column(nameof(ParticipantKey))]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IX_ndstkBooking_ParticipantKey")]
    public Guid? ParticipantKey { get; set; }
```

`Booking/Data/PaymentRecord.cs`, after `MembershipFeeOre`:

```csharp
    /// <summary>
    /// The family supplement part of the total. Kept separate so the backoffice can answer
    /// "how much, and for what" without inferring anything from the total.
    /// </summary>
    [Column(nameof(FamilyFeeOre))]
    public int FamilyFeeOre { get; set; }
```

- [ ] **Step 4: Write the migration**

Create `Booking/Data/Migrations/AddParticipantTable.cs`. Read `Booking/Data/Migrations/AddBookingTables.cs` first and copy its `TableExists` / `CreateIfMissing` helpers and its `AsyncMigrationBase` shape exactly.

```csharp
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Schema only: the participant table, and the two columns the backfill needs somewhere to write.
/// </summary>
/// <remarks>
/// Deliberately does NOT touch the unique index. Swapping it belongs with the backfill, because
/// creating IX_ndstkBooking_OneLivePerParticipantClass while every ParticipantKey is still null
/// does not fail - SQLite treats nulls as distinct in a unique index - it silently produces an
/// index that enforces nothing, and the overbooking guarantee would be gone with no error raised.
/// See NdstkParticipantBackfill.
/// </remarks>
public sealed class AddParticipantTable : AsyncMigrationBase
{
    public AddParticipantTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(BookingTables.Participant) is false)
        {
            Create.Table<ParticipantRecord>().Do();
        }

        AddColumnIfMissing(BookingTables.Booking, "ParticipantKey", "TEXT NULL");
        AddColumnIfMissing(BookingTables.Payment, "FamilyFeeOre", "INTEGER NOT NULL DEFAULT 0");

        return Task.CompletedTask;
    }

    /// <summary>
    /// SQLite has no ADD COLUMN IF NOT EXISTS, and the expression builder's AlterTable throws when
    /// the column is already there, so existence is checked against pragma first.
    /// </summary>
    private void AddColumnIfMissing(string table, string column, string definition)
    {
        var exists = Database
            .Fetch<dynamic>($"PRAGMA table_info({table})")
            .Any(row => string.Equals((string)row.name, column, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            Logger.LogDebug("Column {Table}.{Column} already exists; skipping.", table, column);
            return;
        }

        Database.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private bool TableExists(string tableName)
        => Database.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @0", tableName) > 0;
}
```

- [ ] **Step 5: Register it in the plan**

In `Booking/Data/Migrations/BookingMigrationPlan.cs`, chain the new state after the existing one. Read the file first — the existing `From(...).To<AddBookingTables>("...")` line shows the exact state-string convention. Add:

```csharp
            .To<AddParticipantTable>("{ndstk-participants-v1}");
```

- [ ] **Step 6: Type-check**

Run: `dotnet build -t:CoreCompile`
Expected: no errors. `BookingRepository` will still compile — `ParticipantKey` is nullable and nothing reads it yet.

- [ ] **Step 7: Commit**

```bash
git add Booking/Data
git commit -m "Add the participant table and the columns the backfill will fill"
```

---

### Task 4: Backfill existing members and swap the unique index

**Files:**
- Create: `Booking/Services/NdstkParticipantBackfill.cs`
- Modify: `Booking/BookingComposer.cs`

**Interfaces:**
- Consumes: `ndstkParticipant` from Task 3.
- Produces: every pre-existing member has exactly one participant; every pre-existing booking has a `ParticipantKey`; the index is `IX_ndstkBooking_OneLivePerParticipantClass`.

- [ ] **Step 1: Write the backfill**

Create `Booking/Services/NdstkParticipantBackfill.cs`. Model the key/value guard on `ContentModel/NdstkMemberContentUpgrade.cs`, which uses the same pattern for the same reason.

```csharp
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Services;

/// <summary>
/// Gives every member who registered before participants existed exactly one participant, points
/// their bookings at it, and swaps the one-live-booking index onto the participant.
/// </summary>
/// <remarks>
/// Separate from the migration because it needs IMemberService, which a migration should not reach
/// for. Guarded by a marker in the key/value store and run exactly once, the same pattern
/// NdstkMemberContentUpgrade uses.
///
/// The order is load-bearing. The index swap is LAST, after every ParticipantKey is filled in.
/// Creating a unique index on a column that is null everywhere does not fail in SQLite - nulls are
/// distinct in a unique index - it produces an index that enforces nothing at all, and the
/// overbooking guarantee verified with 60 concurrent attempts would be silently gone.
///
/// Until that last step the old (MemberKey, ClassKey) index is still in place and still enforcing
/// the old rule, so there is no window with no index.
/// </remarks>
internal sealed class NdstkParticipantBackfill(
    IScopeProvider scopeProvider,
    IMemberService memberService,
    IKeyValueService keyValueService,
    ILogger<NdstkParticipantBackfill> logger)
{
    private const string StateKey = "NDSTK/ParticipantBackfill";
    private const string StateValue = "participants-v1";

    private const string OldIndex = "IX_ndstkBooking_OneLivePerMemberClass";
    private const string NewIndex = "IX_ndstkBooking_OneLivePerParticipantClass";

    public void Run()
    {
        if (keyValueService.GetValue(StateKey) == StateValue)
        {
            return;
        }

        using IScope scope = scopeProvider.CreateScope();

        var created = CreateMissingParticipants(scope);
        var pointed = PointBookingsAtParticipants(scope);
        StampSpentWelcomePrices(scope);
        SwapIndex(scope);

        scope.Complete();
        keyValueService.SetValue(StateKey, StateValue);

        logger.LogInformation(
            "Participant backfill complete: {Created} participants created, {Pointed} bookings repointed.",
            created, pointed);
    }

    /// <summary>
    /// One participant per member that has none. The name comes from the email's local part,
    /// which is a guess - so the birth date is left null, and the portal makes the member correct
    /// both before they can book again. Inventing a birth date would be worse than asking.
    /// </summary>
    private int CreateMissingParticipants(IScope scope)
    {
        var created = 0;

        foreach (IMember member in memberService.GetAllMembers())
        {
            var exists = scope.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {BookingTables.Participant} WHERE MemberKey = @0", member.Key) > 0;

            if (exists)
            {
                continue;
            }

            var localPart = member.Email.Split('@')[0];

            scope.Database.Insert(new ParticipantRecord
            {
                Key = Guid.NewGuid(),
                MemberKey = member.Key,
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "Deltagare" : localPart,
                LastName = string.Empty,
                BirthDate = null,
                CreatedUtc = DateTime.UtcNow,
            });

            created++;
        }

        return created;
    }

    private int PointBookingsAtParticipants(IScope scope) => scope.Database.Execute(
        $"""
        UPDATE {BookingTables.Booking}
        SET ParticipantKey = (
            SELECT p.Key FROM {BookingTables.Participant} p
            WHERE p.MemberKey = {BookingTables.Booking}.MemberKey
            ORDER BY p.Id LIMIT 1)
        WHERE ParticipantKey IS NULL
        """);

    /// <summary>
    /// Carries the retired per-account welcome flag onto the participant. The stamp date is the
    /// member's earliest completed payment, because that is when the welcome price was actually
    /// charged; only the null-ness of the column is ever read, so an approximate date is harmless.
    /// </summary>
    private void StampSpentWelcomePrices(IScope scope)
    {
        foreach (IMember member in memberService.GetAllMembers())
        {
            if (member.GetValue<bool>("firstClassDiscountUsed") is false)
            {
                continue;
            }

            scope.Database.Execute(
                $"""
                UPDATE {BookingTables.Participant}
                SET FirstClassUsedUtc = COALESCE(
                    (SELECT MIN(CompletedUtc) FROM {BookingTables.Payment}
                     WHERE MemberKey = @0 AND CompletedUtc IS NOT NULL),
                    @1)
                WHERE MemberKey = @0 AND FirstClassUsedUtc IS NULL
                """,
                member.Key, member.CreateDate);
        }
    }

    private void SwapIndex(IScope scope)
    {
        var unpointed = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {BookingTables.Booking} WHERE ParticipantKey IS NULL");

        if (unpointed > 0)
        {
            // Refuse rather than build an index that enforces nothing. Leaving the old index in
            // place keeps the old, narrower guarantee until this is investigated.
            logger.LogError(
                "{Count} bookings still have no ParticipantKey; leaving the old index in place. "
                + "The one-live-booking rule is still enforced per account, not per child.",
                unpointed);
            return;
        }

        scope.Database.Execute($"DROP INDEX IF EXISTS {OldIndex}");
        scope.Database.Execute(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS {NewIndex}
            ON {BookingTables.Booking} (ParticipantKey, ClassKey)
            WHERE Status IN ('{Domain.BookingStatus.Pending}', '{Domain.BookingStatus.Confirmed}')
            """);
    }
}
```

- [ ] **Step 2: Run it on boot**

In `Booking/BookingComposer.cs`, register `NdstkParticipantBackfill` and call `Run()` from wherever `BookingMigrationRunner` is invoked, immediately after the migration plan executes. Read `Booking/Data/Migrations/BookingMigrationRunner.cs` and `Booking/BookingComposer.cs` first and follow the existing notification-handler wiring rather than inventing a new hook.

- [ ] **Step 3: Back up the database before the first run**

```bash
cp umbraco/Data/Umbraco.sqlite.db "umbraco/Data/Umbraco.sqlite.db.backup-$(date +%Y%m%d-%H%M%S)"
```

- [ ] **Step 4: Run the site and verify the backfill**

Run: `dotnet run`, wait for boot, stop it, then:

```bash
sqlite3 umbraco/Data/Umbraco.sqlite.db "
  SELECT COUNT(*) AS participants FROM ndstkParticipant;
  SELECT COUNT(*) AS unpointed FROM ndstkBooking WHERE ParticipantKey IS NULL;
  SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'IX_ndstkBooking_OneLive%';
"
```

Expected: one participant per member; `unpointed` = 0; exactly one index, named `IX_ndstkBooking_OneLivePerParticipantClass`.

- [ ] **Step 5: Verify it is idempotent**

Run `dotnet run` a second time and re-run the same query. Expected: identical counts — the key/value guard means nothing runs twice.

- [ ] **Step 6: Commit**

```bash
git add Booking
git commit -m "Backfill participants for existing members and swap the booking index"
```

---

### Task 5: The participant repository

**Files:**
- Create: `Booking/Data/IParticipantRepository.cs`, `Booking/Data/ParticipantRepository.cs`
- Modify: `Booking/BookingComposer.cs`

**Interfaces:**
- Consumes: `ParticipantRecord` from Task 3.
- Produces:
  - `Task<IReadOnlyList<ParticipantRecord>> GetForMemberAsync(Guid memberKey)` — live only, oldest first
  - `Task<ParticipantRecord?> GetAsync(Guid participantKey)`
  - `Task<Guid> CreateAsync(Guid memberKey, string firstName, string lastName, DateOnly birthDate, DateTime nowUtc)`
  - `Task<bool> TryUpdateAsync(Guid participantKey, Guid memberKey, string firstName, string lastName, DateOnly birthDate)`
  - `Task<bool> TryRemoveAsync(Guid participantKey, Guid memberKey, DateTime nowUtc)`
  - `Task<bool> TryStampFirstClassUsedAsync(Guid participantKey, DateTime nowUtc)`

- [ ] **Step 1: Write the interface**

Create `Booking/Data/IParticipantRepository.cs`:

```csharp
namespace NDSTK.Booking.Data;

/// <summary>All SQL for participants. Separate from IBookingRepository, which is about places.</summary>
public interface IParticipantRepository
{
    /// <summary>One account's children, oldest first. Removed ones are left out.</summary>
    Task<IReadOnlyList<ParticipantRecord>> GetForMemberAsync(Guid memberKey);

    Task<ParticipantRecord?> GetAsync(Guid participantKey);

    Task<Guid> CreateAsync(
        Guid memberKey, string firstName, string lastName, DateOnly birthDate, DateTime nowUtc);

    /// <summary>
    /// Returns false when the participant is not this member's, so a forged key in a form edits
    /// nothing. The ownership check is in the UPDATE rather than a read-then-write.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid participantKey, Guid memberKey, string firstName, string lastName, DateOnly birthDate);

    /// <summary>Soft delete, so the child's bookings stay readable. Same ownership rule.</summary>
    Task<bool> TryRemoveAsync(Guid participantKey, Guid memberKey, DateTime nowUtc);

    /// <summary>
    /// Marks this child's welcome price spent. Conditional on it still being null, so two payments
    /// settling at once cannot both think they were the first.
    /// </summary>
    Task<bool> TryStampFirstClassUsedAsync(Guid participantKey, DateTime nowUtc);
}
```

- [ ] **Step 2: Implement it**

Create `Booking/Data/ParticipantRepository.cs`. Follow `BookingRepository`'s shape exactly: primary constructor taking `IScopeProvider`, `using IScope scope = scopeProvider.CreateScope(autoComplete: true)` for reads, `CreateScope()` plus `scope.Complete()` for writes.

```csharp
using NDSTK.Booking.Domain;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Data;

/// <summary>
/// NPoco implementation of <see cref="IParticipantRepository"/>, running inside an Umbraco scope so
/// it shares the ambient transaction rather than opening its own.
/// </summary>
public sealed class ParticipantRepository(IScopeProvider scopeProvider) : IParticipantRepository
{
    public async Task<IReadOnlyList<ParticipantRecord>> GetForMemberAsync(Guid memberKey)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<ParticipantRecord>()
            .From<ParticipantRecord>()
            .Where<ParticipantRecord>(record => record.MemberKey == memberKey && record.RemovedUtc == null)
            .OrderBy<ParticipantRecord>(record => record.Id);

        return await scope.Database.FetchAsync<ParticipantRecord>(sql);
    }

    public async Task<ParticipantRecord?> GetAsync(Guid participantKey)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<ParticipantRecord>()
            .From<ParticipantRecord>()
            .Where<ParticipantRecord>(record => record.Key == participantKey);

        return await scope.Database.FirstOrDefaultAsync<ParticipantRecord>(sql);
    }

    public async Task<Guid> CreateAsync(
        Guid memberKey, string firstName, string lastName, DateOnly birthDate, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        var record = new ParticipantRecord
        {
            Key = Guid.NewGuid(),
            MemberKey = memberKey,
            FirstName = firstName,
            LastName = lastName,
            BirthDate = birthDate.ToDateTime(TimeOnly.MinValue),
            CreatedUtc = nowUtc,
        };

        await scope.Database.InsertAsync(record);
        scope.Complete();
        return record.Key;
    }

    public async Task<bool> TryUpdateAsync(
        Guid participantKey, Guid memberKey, string firstName, string lastName, DateOnly birthDate)
    {
        using IScope scope = scopeProvider.CreateScope();

        // Ownership is a condition of the UPDATE, not a check before it: a forged key in a form
        // then edits nothing rather than racing a read.
        var affected = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Participant}
            SET FirstName = @0, LastName = @1, BirthDate = @2
            WHERE Key = @3 AND MemberKey = @4 AND RemovedUtc IS NULL
            """,
            firstName, lastName, birthDate.ToDateTime(TimeOnly.MinValue), participantKey, memberKey);

        scope.Complete();
        return affected > 0;
    }

    public async Task<bool> TryRemoveAsync(Guid participantKey, Guid memberKey, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        var affected = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Participant}
            SET RemovedUtc = @0
            WHERE Key = @1 AND MemberKey = @2 AND RemovedUtc IS NULL
            """,
            nowUtc, participantKey, memberKey);

        scope.Complete();
        return affected > 0;
    }

    public async Task<bool> TryStampFirstClassUsedAsync(Guid participantKey, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        var affected = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Participant}
            SET FirstClassUsedUtc = @0
            WHERE Key = @1 AND FirstClassUsedUtc IS NULL
            """,
            nowUtc, participantKey);

        scope.Complete();
        return affected > 0;
    }
}
```

- [ ] **Step 3: Register it**

In `Booking/BookingComposer.cs`, beside the existing `IBookingRepository` registration:

```csharp
        builder.Services.AddScoped<IParticipantRepository, ParticipantRepository>();
```

- [ ] **Step 4: Type-check**

Run: `dotnet build -t:CoreCompile`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Booking
git commit -m "Add the participant repository"
```

---

### Task 6: Book for a participant

**Files:**
- Modify: `Booking/Data/BookingRepository.cs`, `Booking/Data/IBookingRepository.cs`, `Booking/Services/BookingService.cs`, `Booking/Services/MemberProfileService.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 5.
- Produces:
  - `IBookingRepository.TryReservePlaceAsync(Guid memberKey, Guid participantKey, Guid classKey, DateTime classStartUtc, int capacity, DateTime nowUtc, DateTime holdExpiresUtc)`
  - `BookingService.BookAsync(Guid memberKey, Guid participantKey, Guid classKey, bool useCredit)`
  - `BookingFailure.ParticipantNotFound`, `BookingFailure.ParticipantIncomplete`
  - `MemberProfileService.GetStateAsync(Guid) → MemberState` now returning `IsFamilyAccount`
  - `MemberProfileService.SetFamilyAccountAsync(Guid memberKey)`

- [ ] **Step 1: Carry the participant through the repository**

In `Booking/Data/BookingRepository.cs`:

`ToSnapshot` gains the participant. Note the `?? Guid.Empty` — a backfilled row can only be null if the backfill refused to complete, and `Guid.Empty` matches no participant, which fails closed:

```csharp
    private static BookingSnapshot ToSnapshot(BookingRecord record) => new(
        record.Id,
        record.MemberKey,
        record.ParticipantKey ?? Guid.Empty,
        record.ClassKey,
        record.Status,
        record.HoldExpiresUtc,
        record.ClassStartUtc,
        record.ReminderSentUtc);
```

`TryReservePlaceAsync` takes `Guid participantKey` after `memberKey`. Three statements change inside it:

The stale-hold credit refund and the stale-hold expiry both move from `MemberKey = @0` to `ParticipantKey = @0`, and are passed `participantKey`. This must match the new index: the index now treats a `(ParticipantKey, ClassKey)` pair as the live-booking unit, so the cleanup that clears the way for a rebooking has to use the same unit or the INSERT below still trips.

The INSERT gains the column:

```csharp
            inserted = await scope.Database.ExecuteAsync(
                $"""
                INSERT INTO {BookingTables.Booking}
                    (MemberKey, ParticipantKey, ClassKey, ClassStartUtc, Status, CreatedUtc, HoldExpiresUtc)
                SELECT @0, @1, @2, @3, @4, @5, @6
                WHERE (
                    SELECT COUNT(*) FROM {BookingTables.Booking}
                    WHERE ClassKey = @2
                      AND (Status = @7 OR (Status = @4 AND HoldExpiresUtc > @5))
                ) < @8
                """,
                memberKey, participantKey, classKey, classStartUtc, Domain.BookingStatus.Pending,
                nowUtc, holdExpiresUtc, Domain.BookingStatus.Confirmed, capacity);
```

Renumber every parameter carefully — the capacity subquery must still reference the class key, now `@2`, and the "now" used for the hold comparison must still be `@5`. Getting this wrong does not fail loudly; it silently miscounts the class.

Update the `UNIQUE` catch's log message to name the participant.

- [ ] **Step 2: Update `MemberProfileService`**

Replace `FirstClassDiscountUsedAlias` with the family flag, and drop `MarkFirstClassDiscountUsedAsync` — the participant repository owns that now:

```csharp
    internal const string MembershipPaidUntilAlias = "membershipPaidUntil";
    internal const string FamilyAccountAlias = "familjekonto";

    public async Task<MemberState> GetStateAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogWarning("Member {MemberKey} was not found; treating them as new.", memberKey);
            return new MemberState(null, IsFamilyAccount: false);
        }

        return new MemberState(ReadPaidUntil(member), member.GetValue<bool>(FamilyAccountAlias));
    }

    /// <summary>
    /// Turns an account into a family account. Deliberately does not touch the expiry date - see
    /// Pricing.FamilyUpgradeQuote for why moving it would make the supplement a cheap renewal.
    /// </summary>
    public async Task SetFamilyAccountAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Cannot upgrade {MemberKey} to a family account: not found.", memberKey);
            return;
        }

        member.SetValue(FamilyAccountAlias, true);
        memberService.Save(member);

        logger.LogInformation("Member {MemberKey} is now a family account.", memberKey);
    }
```

- [ ] **Step 3: Update `BookingService.BookAsync`**

Add the two failures to the enum:

```csharp
    ParticipantNotFound,
    ParticipantIncomplete,
```

Take `IParticipantRepository participants` in the primary constructor, and change the signature to `BookAsync(Guid memberKey, Guid participantKey, Guid classKey, bool useCredit)`. Immediately after the class checks, add:

```csharp
        ParticipantRecord? participant = await participants.GetAsync(participantKey);

        // Ownership is checked here rather than trusted from the form: the key comes off a POST.
        if (participant is null || participant.MemberKey != memberKey || participant.RemovedUtc is not null)
        {
            return new BookingAttempt(BookingFailure.ParticipantNotFound);
        }

        // Only ever true for a child the backfill created, who has no real birth date yet. Asking
        // for it once is better than carrying a guessed one through the club's records.
        if (participant.BirthDate is null)
        {
            return new BookingAttempt(BookingFailure.ParticipantIncomplete);
        }
```

Change `Capacity.HasLiveBooking(forClass, memberKey, nowUtc)` to pass `participantKey`, in **both** places it appears. Change the quote to:

```csharp
        MemberState member = await profiles.GetStateAsync(memberKey);
        var participantState = new ParticipantState(participant.FirstClassUsedUtc is not null);
        BookingQuote quote = Pricing.Quote(
            member, participantState, config.Prices, credit is not null, today);
```

Pass `participantKey` to `TryReservePlaceAsync`. Set the family fee on the payment record:

```csharp
            MembershipFeeOre = quote.MembershipDueOre,
            FamilyFeeOre = quote.FamilyDueOre,
            ClassFeeOre = quote.ClassFeeOre,
```

- [ ] **Step 4: Update `SettlePaymentAsync`**

The welcome-price stamp moves to the participant, reached through the booking. Replace the whole `MemberState after = ...` block with:

```csharp
        // Deliberately not "did this payment equal the welcome price". Comparing the stored amount
        // against the configured price would break the moment an editor changes prices between a
        // booking and its payment. Instead: Pricing only ever quotes the welcome price while the
        // child's FirstClassUsedUtc is still null, so a class fee charged to a child whose stamp is
        // null *was* the welcome price, whatever the numbers now say. The stamp is conditional, so
        // two payments settling at once cannot both claim to have been the first.
        if (payment.ClassFeeOre > 0 && payment.BookingId is { } stampBookingId)
        {
            BookingRecord? booking = await repository.GetBookingAsync(stampBookingId);
            if (booking?.ParticipantKey is { } participantKey)
            {
                await participants.TryStampFirstClassUsedAsync(participantKey, nowUtc);
            }
        }

        // The family supplement is only ever charged alongside the annual fee on a renewal, or on
        // its own as an upgrade. Either way, paying it makes the account a family account.
        if (payment.FamilyFeeOre > 0)
        {
            await profiles.SetFamilyAccountAsync(payment.MemberKey);
        }
```

Leave the `payment.MembershipFeeOre > 0 → ExtendMembershipAsync` branch exactly as it is. That is what keeps the upgrade from moving the date: an upgrade payment has `MembershipFeeOre = 0`.

- [ ] **Step 5: Fix the call sites**

`Booking/Web/BookingSurfaceController.cs` will not compile — `BookAsync` has a new parameter. Task 11 gives it a real child picker; for now pass the member's only participant so the site still runs:

```csharp
        IReadOnlyList<ParticipantRecord> children = await participants.GetForMemberAsync(memberKey);
        if (children.Count != 1)
        {
            // Task 11 replaces this with a picker.
            return Fail("Välj vilket barn bokningen gäller.");
        }
```

Add Swedish messages for the two new failures wherever `BookingFailure` is translated:

```csharp
        BookingFailure.ParticipantNotFound => "Deltagaren hittades inte.",
        BookingFailure.ParticipantIncomplete => "Fyll i barnets födelsedatum innan du bokar.",
```

- [ ] **Step 6: Type-check and test**

Run: `dotnet build -t:CoreCompile` then `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: no errors, all tests pass.

- [ ] **Step 7: Runtime check — two siblings on one class**

Run the site. In `sqlite3`, add a second participant to an existing member by hand, then book both onto the same class through the portal. Expected: both bookings confirm, and the class shows two fewer places. Before this task that second insert would have been rejected by the index.

- [ ] **Step 8: Commit**

```bash
git add Booking
git commit -m "Book a place for a child rather than for an account"
```

---

## Phase 9 — Member portal

### Task 7: The member type and Settings fields

**Files:**
- Modify: `ContentModel/NdstkContentModelInstaller.cs`, `Booking/Services/MembershipSettings.cs`, `Booking/Services/MembershipSettingsService.cs`

**Interfaces:**
- Consumes: `PriceList.FamilyFeeOre` from Task 1.
- Produces: member properties `familjekonto` (True/false) and `telefon` (Textstring); Settings property `familyFee` (Numeric); `MembershipSettings.Defaults.Prices.FamilyFeeOre == 100 * 100`.

- [ ] **Step 1: Add the Settings field**

In `ContentModel/NdstkContentModelInstaller.cs`, inside the existing `EnsureGroupAsync` call for the `membership` group, add after `membershipFee` and renumber the sort orders of everything below it:

```csharp
            factory.Property(BuiltInDataTypes.Numeric, "familyFee", "Familjetillägg (kr)", "Tillägg för familjekonto, per år. Standard: 100.", 1),
```

- [ ] **Step 2: Add the member properties**

In the same file, extend the existing `EnsureMemberPropertiesAsync` call. `familjekonto` is administrative — a member who could edit it would get a family account free — so it is `canView: true, canEdit: false`, matching `membershipPaidUntil`. `telefon` is the member's own contact detail and nothing about it is worth money, so they may edit it.

```csharp
            (factory.Property(BuiltInDataTypes.TrueFalse, "familjekonto", "Family account", "Set once the family supplement is paid. Allows more than one participant.", 12), true, false),
            (factory.Property(BuiltInDataTypes.Textstring, "telefon", "Phone", "The guardian's phone number, shown on the class roster.", 13), true, true),
```

Leave `firstClassDiscountUsed` declared. Removing it would delete values the backfill in Task 4 reads on an already-installed site; nothing writes it after that, and it costs one unused column.

- [ ] **Step 3: Add the default**

`Booking/Services/MembershipSettings.cs`:

```csharp
        new PriceList(
            MembershipFeeOre: 150 * 100,
            FamilyFeeOre: 100 * 100,
            FirstClassPriceOre: 100 * 100,
            ClassPriceOre: 200 * 100),
```

- [ ] **Step 4: Read it**

`Booking/Services/MembershipSettingsService.cs`, inside `Get()`:

```csharp
                FamilyFeeOre: KronorToOre(settings, "familyFee", defaults.FamilyFeeOre),
```

A zero still counts as "not set" and falls back, per the existing rule.

- [ ] **Step 5: Verify at runtime**

Run the site, open **Settings → Medlemskap** in the backoffice. Expected: *Familjetillägg (kr)* appears between *Årsavgift* and *Pris första klassen*. Open a member. Expected: *Family account* and *Phone* under Membership.

- [ ] **Step 6: Commit**

```bash
git add ContentModel Booking
git commit -m "Add the family account flag, the guardian phone and the family supplement price"
```

---

### Task 8: Registration collects the guardian and the first child

**Files:**
- Create: `Booking/Web/SwedishDate.cs`
- Modify: `Booking/Web/RegisterFormModel.cs`, `Booking/Web/RegisterSurfaceController.cs`, `Views/MemberRegister.cshtml`

**Interfaces:**
- Consumes: `IParticipantRepository.CreateAsync` from Task 5.
- Produces: `SwedishDate.TryParseCompact(string?, out DateOnly)`, `SwedishDate.ToCompact(DateOnly)`, `SwedishDate.AgeOn(DateOnly birthDate, DateOnly on)`; `RegisterFormModel` with `FirstName`, `LastName`, `Phone`, `ChildFirstName`, `ChildLastName`, `ChildBirthDate`.

- [ ] **Step 1: Write the date helper**

Create `Booking/Web/SwedishDate.cs`:

```csharp
using System.Globalization;

namespace NDSTK.Booking.Web;

/// <summary>
/// The eight-digit ÅÅÅÅMMDD form a Swedish parent will type without being asked, because it is the
/// first eight digits of a personnummer.
/// </summary>
/// <remarks>
/// Only the date is ever taken. No personnummer is collected or stored, so a twelve-digit value is
/// rejected rather than silently truncated - accepting it would invite people to type it.
/// </remarks>
public static class SwedishDate
{
    public static bool TryParseCompact(string? value, out DateOnly date)
    {
        date = default;

        var trimmed = value?.Trim();

        // The length check is what rejects a full personnummer; TryParseExact is what rejects
        // "2026ab01" and impossible dates like 20261301.
        return trimmed is { Length: 8 }
               && DateOnly.TryParseExact(
                   trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static string ToCompact(DateOnly date)
        => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Whole years, counted on a given day rather than today, so a roster can age a child on the class date.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly on)
    {
        var age = on.Year - birthDate.Year;
        return birthDate > on.AddYears(-age) ? age - 1 : age;
    }
}
```

- [ ] **Step 2: Extend the form model**

In `Booking/Web/RegisterFormModel.cs`, add above the honeypot:

```csharp
    [Required(ErrorMessage = "Ange ditt förnamn.")]
    [StringLength(100)]
    [Display(Name = "Ditt förnamn")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange ditt efternamn.")]
    [StringLength(100)]
    [Display(Name = "Ditt efternamn")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange ditt telefonnummer.")]
    [StringLength(30)]
    [Display(Name = "Telefon")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets förnamn.")]
    [StringLength(100)]
    [Display(Name = "Barnets förnamn")]
    public string ChildFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets efternamn.")]
    [StringLength(100)]
    [Display(Name = "Barnets efternamn")]
    public string ChildLastName { get; set; } = string.Empty;

    /// <summary>Eight digits, ÅÅÅÅMMDD. The real date check lives in the controller.</summary>
    [Required(ErrorMessage = "Ange barnets födelsedatum.")]
    [Display(Name = "Barnets födelsedatum (ÅÅÅÅMMDD)")]
    public string ChildBirthDate { get; set; } = string.Empty;
```

- [ ] **Step 3: Wire it into registration**

In `Booking/Web/RegisterSurfaceController.cs`, inject `IParticipantRepository participants` and `IMemberService memberService`.

Validate the birth date **after** the existing honeypot and timestamp checks and **before** `memberManager.CreateAsync`, so the password-errors-before-duplicate-address ordering that keeps the responses leak-free is untouched:

```csharp
        if (SwedishDate.TryParseCompact(form.ChildBirthDate, out DateOnly childBirthDate) is false)
        {
            ModelState.AddModelError(
                nameof(form.ChildBirthDate), "Skriv födelsedatumet som ÅÅÅÅMMDD, till exempel 20170413.");
            return CurrentUmbracoPage();
        }

        if (childBirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(form.ChildBirthDate), "Födelsedatumet ligger i framtiden.");
            return CurrentUmbracoPage();
        }
```

Then, inside the existing `if (created.Succeeded)` branch and before the mail is sent:

```csharp
        if (created.Succeeded)
        {
            // The member's Name is what the backoffice member list shows. Left as the email it
            // would be a list of addresses, which is not something anyone can administer.
            IMember? member = memberService.GetByKey(user.Key);
            if (member is not null)
            {
                member.Name = $"{form.FirstName.Trim()} {form.LastName.Trim()}";
                member.SetValue("telefon", form.Phone.Trim());
                memberService.Save(member);
            }

            await participants.CreateAsync(
                user.Key,
                form.ChildFirstName.Trim(),
                form.ChildLastName.Trim(),
                childBirthDate,
                DateTime.UtcNow);

            await SendVerificationMailAsync(user);
            TempData["RegisterMessage"] = CheckYourInboxMessage;
            return RedirectToCurrentUmbracoPage();
        }
```

The participant is written only on success, so a duplicate address still gets the same "check your inbox" response and writes nothing. Registration stays enumeration-resistant.

- [ ] **Step 4: Add the fields to the view**

In `Views/MemberRegister.cshtml`, add two fieldsets before the password fields, following the markup and validation-message pattern the existing email field uses. Group them under `<legend>Du</legend>` and `<legend>Barnet</legend>` so it is obvious which name is whose. Give the birth date `inputmode="numeric"`, `maxlength="8"`, `pattern="\d{8}"` and `placeholder="ÅÅÅÅMMDD"`.

- [ ] **Step 5: Verify at runtime**

Register a new account end to end. Expected: the verification mail lands in `umbraco/Logs/Mail`; the member appears in the backoffice under their real name with a phone number; and

```bash
sqlite3 umbraco/Data/Umbraco.sqlite.db "SELECT FirstName, LastName, BirthDate FROM ndstkParticipant ORDER BY Id DESC LIMIT 1;"
```

shows the child. Then submit the form with `ChildBirthDate` = `20261301` and expect the ÅÅÅÅMMDD error rather than a crash.

- [ ] **Step 6: Commit**

```bash
git add Booking Views
git commit -m "Collect the guardian's details and the first child at registration"
```

---

### Task 9: Mina barn

**Files:**
- Create: `Booking/Web/ParticipantFormModel.cs`, `Booking/Web/ParticipantSurfaceController.cs`, `Views/Partials/MemberChildren.cshtml`
- Modify: `Booking/Web/MemberPortalViewModel.cs`, `Booking/Web/MemberPortalController.cs`, `Views/MemberPortal.cshtml`

**Interfaces:**
- Consumes: Tasks 5, 7, 8.
- Produces: `MemberPortalViewModel.Children` (`IReadOnlyList<MemberChildRow>`) and `.CanAddChild`; `MemberChildRow(Guid Key, string FirstName, string LastName, DateOnly? BirthDate, bool FirstClassAvailable)`; `MembershipStatus.IsFamilyAccount`.

- [ ] **Step 1: The form model**

Create `Booking/Web/ParticipantFormModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace NDSTK.Booking.Web;

/// <summary>Adding or editing one child. The key is empty when adding.</summary>
public sealed class ParticipantFormModel
{
    public Guid Key { get; set; }

    [Required(ErrorMessage = "Ange barnets förnamn.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets efternamn.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ange barnets födelsedatum.")]
    public string BirthDate { get; set; } = string.Empty;
}
```

- [ ] **Step 2: The controller**

Create `Booking/Web/ParticipantSurfaceController.cs`. Follow `Booking/Web/BookingSurfaceController.cs` exactly for the base class, the `[ValidateAntiForgeryToken]` attributes, the signed-in-member lookup and the `TempData` message convention.

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(ParticipantFormModel form)
    {
        Guid memberKey = /* signed-in member, as BookingSurfaceController resolves it */;

        // The family supplement is what buys more than one child. This is the real rule; the view
        // only hides the button, and a hidden button is not a rule.
        MemberState member = await profiles.GetStateAsync(memberKey);
        IReadOnlyList<ParticipantRecord> existing = await participants.GetForMemberAsync(memberKey);

        if (member.IsFamilyAccount is false && existing.Count >= 1)
        {
            TempData["ChildMessage"] = "Uppgradera till familjekonto för att lägga till fler barn.";
            return RedirectToCurrentUmbracoPage();
        }

        if (SwedishDate.TryParseCompact(form.BirthDate, out DateOnly birthDate) is false)
        {
            TempData["ChildMessage"] = "Skriv födelsedatumet som ÅÅÅÅMMDD.";
            return RedirectToCurrentUmbracoPage();
        }

        await participants.CreateAsync(
            memberKey, form.FirstName.Trim(), form.LastName.Trim(), birthDate, DateTime.UtcNow);

        TempData["ChildMessage"] = $"{form.FirstName.Trim()} är tillagd.";
        return RedirectToCurrentUmbracoPage();
    }
```

`Edit` parses the date the same way, calls `TryUpdateAsync(form.Key, memberKey, ...)` and reports `"Ändringen sparades."` or `"Barnet hittades inte."`. `Remove` calls `TryRemoveAsync(key, memberKey, DateTime.UtcNow)` and reports `"Barnet togs bort."` Both pass `memberKey`, so a forged key in a POST changes nothing — the ownership check is the UPDATE's `WHERE`, not a read before it.

`Remove` refuses the last remaining child first, because an account with no participants can never book:

```csharp
        if (existing.Count <= 1)
        {
            TempData["ChildMessage"] = "Kontot måste ha minst ett barn.";
            return RedirectToCurrentUmbracoPage();
        }
```

- [ ] **Step 3: Carry children on the view model**

In `Booking/Web/MemberPortalViewModel.cs`, add two members to the record and remove `FirstClassDiscountAvailable` — it has no meaning per account any more, and leaving it would let a view quote the wrong price:

```csharp
    IReadOnlyList<MemberChildRow> Children,
```

```csharp
    /// <summary>A solo account may have exactly one child; a family account may add more.</summary>
    public bool CanAddChild => Membership.IsFamilyAccount || Children.Count == 0;
```

Add the row record below it:

```csharp
/// <summary>One row in "Mina barn".</summary>
public sealed record MemberChildRow(
    Guid Key, string FirstName, string LastName, DateOnly? BirthDate, bool FirstClassAvailable)
{
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// False only for a child the backfill created, who has no real birth date yet. Booking is
    /// refused until it is filled in - see BookingFailure.ParticipantIncomplete.
    /// </summary>
    public bool IsComplete => BirthDate is not null;

    public string BirthDateCompact => BirthDate is { } date ? SwedishDate.ToCompact(date) : string.Empty;

    public int? Age => BirthDate is { } date
        ? SwedishDate.AgeOn(date, DateOnly.FromDateTime(DateTime.UtcNow))
        : null;
}
```

Add `bool IsFamilyAccount` to `MembershipStatus` and fill it from `MemberState.IsFamilyAccount`.

- [ ] **Step 4: Load them in the controller**

In `Booking/Web/MemberPortalController.cs`, inject `IParticipantRepository` and map its rows into the view model beside the existing bookings load:

```csharp
        IReadOnlyList<ParticipantRecord> children = await participants.GetForMemberAsync(memberKey);
        IReadOnlyList<MemberChildRow> childRows =
        [
            .. children.Select(child => new MemberChildRow(
                child.Key,
                child.FirstName,
                child.LastName,
                child.BirthDate is { } date ? DateOnly.FromDateTime(date) : null,
                child.FirstClassUsedUtc is null)),
        ];
```

- [ ] **Step 5: The partial**

Create `Views/Partials/MemberChildren.cshtml`, following `Views/Partials/MemberBookings.cshtml` for markup and antiforgery. It renders `<h2>Mina barn</h2>`, then per child: name, ÅÅÅÅMMDD, age, an inline edit form and a remove button. When `CanAddChild` is true it shows the add form; when false it shows the upgrade call to action Task 10 wires up. Any child whose `IsComplete` is false gets "Fyll i födelsedatum för att kunna boka."

Render it from `Views/MemberPortal.cshtml` above "Mina bokningar", and show `TempData["ChildMessage"]` at its top.

- [ ] **Step 6: Verify at runtime**

On a solo account: no add form. POST to `Add` anyway with a valid antiforgery token and expect the upgrade message, not a new row. Edit a child and confirm the change in `ndstkParticipant`. Try removing the only child and expect the refusal.

- [ ] **Step 7: Commit**

```bash
git add Booking Views
git commit -m "Add Mina barn to the member portal"
```

---

### Task 10: The family upgrade purchase

**Files:**
- Create: `Booking/Web/FamilyUpgradeSurfaceController.cs`
- Modify: `Views/Partials/MemberChildren.cshtml`, `Views/SwishPayment.cshtml`

**Interfaces:**
- Consumes: `Pricing.FamilyUpgradeQuote` (Task 1), `MemberProfileService.SetFamilyAccountAsync` (Task 6).
- Produces: a POST creating a `PaymentRecord` with `BookingId = null`, `MembershipFeeOre = 0`, `ClassFeeOre = 0`, `FamilyFeeOre = familyFee`, then redirecting to the Swish page.

- [ ] **Step 1: Write the controller**

Create `Booking/Web/FamilyUpgradeSurfaceController.cs`, following `Booking/Web/BookingSurfaceController.cs` for the base class, antiforgery and how it resolves the payment page URL with `?ref=`:

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upgrade()
    {
        Guid memberKey = /* signed-in member */;

        MemberState member = await profiles.GetStateAsync(memberKey);
        if (member.IsFamilyAccount)
        {
            TempData["ChildMessage"] = "Kontot är redan ett familjekonto.";
            return RedirectToCurrentUmbracoPage();
        }

        MembershipSettings config = settings.Get();
        BookingQuote quote = Pricing.FamilyUpgradeQuote(config.Prices);

        // No booking: this is a purchase of its own. SettlePaymentAsync already handles a null
        // BookingId, and because MembershipFeeOre is zero it will not extend the expiry date -
        // which is the whole point. See Pricing.FamilyUpgradeQuote.
        var payment = new PaymentRecord
        {
            Reference = Guid.NewGuid(),
            MemberKey = memberKey,
            BookingId = null,
            AmountOre = quote.TotalOre,
            MembershipFeeOre = 0,
            FamilyFeeOre = quote.FamilyDueOre,
            ClassFeeOre = 0,
            Status = PaymentStatus.Pending,
            Provider = paymentProvider.Name,
            CreatedUtc = DateTime.UtcNow,
        };

        await repository.CreatePaymentAsync(payment);

        logger.LogInformation("Family upgrade payment {Reference} created for {MemberKey}.",
            payment.Reference, memberKey);

        return Redirect($"{PaymentPageUrl()}?ref={payment.Reference}");
    }
```

- [ ] **Step 2: Make the payment page describe it**

`Views/SwishPayment.cshtml` names a class today, and a payment with no booking has none. Branch on it: when the payment has no booking, the heading reads "Familjekonto" and the line item reads "Familjetillägg, ett år". Wherever the total is broken down, show the three-way split — årsavgift, familjetillägg, klassavgift — skipping any part that is zero.

- [ ] **Step 3: Add the button**

In `Views/Partials/MemberChildren.cshtml`, where `CanAddChild` is false, render a POST form to the upgrade action reading `Uppgradera till familjekonto — @(Model.Prices.FamilyFeeOre / 100) kr/år`, with a line explaining that it lets the account add more children and **does not change the membership expiry date**.

- [ ] **Step 4: Verify at runtime**

Upgrade a solo account through the mocked Swish page. Expected: `familjekonto` becomes true, `membershipPaidUntil` is **unchanged**, the add-child form appears, and

```bash
sqlite3 umbraco/Data/Umbraco.sqlite.db "SELECT AmountOre, MembershipFeeOre, FamilyFeeOre, ClassFeeOre, Status FROM ndstkPayment ORDER BY Id DESC LIMIT 1;"
```

shows `10000|0|10000|0|Paid`. Then start a second upgrade and press the mock's failure button: the flag must not move.

- [ ] **Step 5: Commit**

```bash
git add Booking Views
git commit -m "Sell the family account upgrade through the Swish mock"
```

---

### Task 11: The child picker on booking

**Files:**
- Modify: `Booking/Web/BookingSurfaceController.cs`, `Booking/Web/MemberPortalViewModel.cs`, `Views/MemberPortal.cshtml`

**Interfaces:**
- Consumes: Tasks 6, 9.
- Produces: the booking POST carries `participantKey`; `MemberPortalViewModel.NextClassFeeOreFor(MemberChildRow)` and `.NextBookingTotalOreFor(MemberChildRow)`.

- [ ] **Step 1: Take the key on the POST**

In `Booking/Web/BookingSurfaceController.cs`, replace the Task 6 placeholder. The action takes `Guid participantKey` and passes it straight to `BookAsync`. When the member has exactly one live child the view posts that child's key in a hidden field, so there is no special case here — one code path, always a key. Ownership is verified inside `BookAsync`, which is where it belongs.

- [ ] **Step 2: Quote per child in the portal**

`NextClassFeeOre` and `NextBookingTotalOre` read a per-account discount flag that no longer exists. Replace both with methods that take a child:

```csharp
    /// <summary>The class fee alone for this child's next booking.</summary>
    public int NextClassFeeOreFor(MemberChildRow child) => child.FirstClassAvailable
        ? Prices.FirstClassPriceOre
        : Prices.ClassPriceOre;

    /// <summary>
    /// What the member will actually be charged for this child's next booking, membership and
    /// family fees included when they are due. This is what the booking button shows: quoting the
    /// class fee alone and then presenting a larger figure on the payment page reads as a bait and
    /// switch.
    /// </summary>
    public int NextBookingTotalOreFor(MemberChildRow child)
    {
        var membershipDue = Membership.IsValid ? 0 : Prices.MembershipFeeOre;
        var familyDue = Membership.IsValid || Membership.IsFamilyAccount is false
            ? 0
            : Prices.FamilyFeeOre;

        return NextClassFeeOreFor(child) + membershipDue + familyDue;
    }
```

That mirrors `Pricing.Quote` exactly. If the two ever disagree the member is quoted one price and charged another, so keep them side by side when either changes.

- [ ] **Step 3: Render the picker**

In `Views/MemberPortal.cshtml`, inside each class's booking form:

- One live child: a hidden `participantKey` and a button reading `Boka — X kr`.
- Several: a `<select name="participantKey">` and a button reading `Boka`. Because the price depends on which child is chosen, each option's label carries its own price — `Elsa Svensson — 100 kr` — rather than one figure on the button that would be wrong for at least one child.

Children whose `IsComplete` is false are listed but `disabled`, with the reason beside them.

- [ ] **Step 4: Verify at runtime**

On a family account with two children where only one has used their welcome price: the select shows two different prices. Book the cheaper one and confirm the payment page's amount matches the label. Afterwards, confirm the other child's price is unchanged — the welcome price is per child.

- [ ] **Step 5: Commit**

```bash
git add Booking Views
git commit -m "Choose which child a booking is for"
```

---

## Phase 10 — Backoffice

### Task 12: The reporting queries and the management API

**Files:**
- Create: `Booking/Admin/MemberAdminRow.cs`, `Booking/Admin/MemberAdminDetail.cs`, `Booking/Admin/ClassRosterRow.cs`, `Booking/Admin/MemberAdminQueries.cs`, `Booking/Admin/MemberAdminController.cs`
- Modify: `Booking/BookingComposer.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `GET /umbraco/management/api/v1/backoffice/ndstk/members` → `MemberAdminRow[]`; `GET .../members/{memberKey}` → `MemberAdminDetail`; `GET .../members/roster/{classKey}` → `ClassRosterRow[]`.

- [ ] **Step 1: The DTOs**

Create `Booking/Admin/MemberAdminRow.cs`:

```csharp
namespace NDSTK.Booking.Admin;

/// <summary>One account, as the Medlemmar dashboard lists it.</summary>
public sealed record MemberAdminRow(
    Guid MemberKey,
    string Name,
    string Email,
    string? Phone,
    bool IsFamilyAccount,
    DateTime? VerifiedUtc,
    DateTime? MemberSinceUtc,
    DateOnly? PaidUntil,
    int TotalPaidOre,
    DateTime? LastPaymentUtc,
    int ParticipantCount,
    int ConfirmedBookings,
    int CancelledBookings,
    int ExpiredBookings,
    int UnspentCredits,
    IReadOnlyList<string> ChildNames)
{
    /// <summary>
    /// Negative once the membership has lapsed, which the dashboard renders as "Utgången" rather
    /// than as a negative number of days.
    /// </summary>
    public int? DaysLeft => PaidUntil is { } until
        ? until.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber
        : null;
}
```

Create `Booking/Admin/ClassRosterRow.cs`:

```csharp
namespace NDSTK.Booking.Admin;

/// <summary>One line of a class roster: the child, and how to reach their guardian.</summary>
public sealed record ClassRosterRow(
    int BookingId,
    string ChildName,
    int? Age,
    string GuardianName,
    string GuardianEmail,
    string? GuardianPhone,
    string Status,
    DateTime CreatedUtc);
```

Create `Booking/Admin/MemberAdminDetail.cs`:

```csharp
namespace NDSTK.Booking.Admin;

/// <summary>Everything about one account, for the dashboard's detail panel.</summary>
public sealed record MemberAdminDetail(
    MemberAdminRow Summary,
    IReadOnlyList<AdminPaymentRow> Payments,
    IReadOnlyList<AdminBookingRow> Bookings);

/// <summary>
/// One payment, with the split intact. Kept split rather than reduced to a total so the club can
/// answer "how much, and for what" without inferring anything.
/// </summary>
public sealed record AdminPaymentRow(
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    int AmountOre,
    int MembershipFeeOre,
    int FamilyFeeOre,
    int ClassFeeOre,
    string Status,
    string Provider);

public sealed record AdminBookingRow(
    string ChildName,
    string ClassName,
    DateTime ClassStartUtc,
    string Status);
```

- [ ] **Step 2: The queries**

Create `Booking/Admin/MemberAdminQueries.cs`: read-only SQL plus `IMemberService` for the member facts. Kept out of `IBookingRepository` so the booking path's interface does not grow a reporting surface.

The counts come from grouped queries rather than one query per member — a club with 200 members must not issue 200 round trips:

```csharp
    private const string CountsSql = """
        SELECT MemberKey,
               SUM(CASE WHEN Status = @0 THEN 1 ELSE 0 END) AS Confirmed,
               SUM(CASE WHEN Status = @1 THEN 1 ELSE 0 END) AS Cancelled,
               SUM(CASE WHEN Status = @2 THEN 1 ELSE 0 END) AS Expired
        FROM ndstkBooking
        GROUP BY MemberKey
        """;

    private const string PaymentTotalsSql = """
        SELECT MemberKey,
               SUM(AmountOre)   AS TotalPaidOre,
               MAX(CompletedUtc) AS LastPaymentUtc,
               MIN(CASE WHEN MembershipFeeOre > 0 THEN CompletedUtc END) AS MemberSinceUtc
        FROM ndstkPayment
        WHERE Status = @0
        GROUP BY MemberKey
        """;

    private const string CreditsSql = """
        SELECT MemberKey, COUNT(*) AS Unspent
        FROM ndstkBookingCredit
        WHERE SpentOnBookingId IS NULL
        GROUP BY MemberKey
        """;

    private const string ChildrenSql = """
        SELECT MemberKey, FirstName, LastName
        FROM ndstkParticipant
        WHERE RemovedUtc IS NULL
        ORDER BY Id
        """;
```

Fetch each into a dictionary keyed by `MemberKey`, then walk `memberService.GetAllMembers()` once and project a `MemberAdminRow` per member, defaulting missing dictionary entries to zero.

`MemberSinceUtc` falls back to the member's `CreateDate` when no payment ever included a membership fee — a comped membership has no payment.

`VerifiedUtc` reads `EmailConfirmedDate`. **Confirm at implementation which type declares it**: it is present in `Umbraco.Core.dll` 18.1.1, but if it is not reachable from `IMember`, add a `verifieradUtc` member property in Task 7's style and stamp it in `MemberVerifyController.ConfirmAsync` immediately after `ConfirmEmailAsync` succeeds; the query then reads that. Do not guess — compile it.

The roster is one join plus a member lookup for the guardian:

```csharp
    private const string RosterSql = """
        SELECT b.Id, b.Status, b.CreatedUtc, b.MemberKey,
               p.FirstName, p.LastName, p.BirthDate
        FROM ndstkBooking b
        JOIN ndstkParticipant p ON p.Key = b.ParticipantKey
        WHERE b.ClassKey = @0 AND b.Status IN (@1, @2)
        ORDER BY p.FirstName, p.LastName
        """;
```

passing `BookingStatus.Confirmed` and `BookingStatus.Pending` — a place being held for an unpaid booking is still a place taken, which is what `Capacity.HoldsPlace` says too.

- [ ] **Step 3: The controller**

Create `Booking/Admin/MemberAdminController.cs`, copying the attribute set from `c:\src\Esatto.Packages\Esatto.Umbraco.Backoffice.Redirects\src\RedirectsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace NDSTK.Booking.Admin;

/// <summary>
/// Read-only management API behind the Medlemmar dashboard and the class roster.
/// </summary>
/// <remarks>
/// Gated on SectionAccessMembers, so authorisation is Umbraco's rather than a check of ours. There
/// are deliberately no write endpoints: members manage their own children, and an admin correcting
/// a birth date is a separate request.
/// </remarks>
[ApiController]
[VersionedApiBackOfficeRoute("backoffice/ndstk/members")]
[ApiExplorerSettings(GroupName = "NDSTK Member Administration")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessMembers)]
public sealed class MemberAdminController(MemberAdminQueries queries) : ManagementApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MemberAdminRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await queries.GetMembersAsync());

    [HttpGet("{memberKey:guid}")]
    [ProducesResponseType(typeof(MemberAdminDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOne(Guid memberKey)
        => await queries.GetDetailAsync(memberKey) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("roster/{classKey:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ClassRosterRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoster(Guid classKey)
        => Ok(await queries.GetRosterAsync(classKey));
}
```

Register `MemberAdminQueries` as scoped in `Booking/BookingComposer.cs`.

- [ ] **Step 4: Verify the endpoints**

Run the site, sign in to the backoffice, then in the browser console:

```js
await (await fetch('/umbraco/management/api/v1/backoffice/ndstk/members', { credentials: 'include' })).json()
```

Expected: an array of rows with real names, totals and counts. Cross-check one row against the database by hand. Then open the same URL in a private window with no backoffice session and expect 401.

- [ ] **Step 5: Commit**

```bash
git add Booking
git commit -m "Add the member administration read model and management API"
```

---

### Task 13: The Medlemmar dashboard

**Files:**
- Create: `wwwroot/App_Plugins/NDSTK.MemberAdmin/umbraco-package.json`, `wwwroot/App_Plugins/NDSTK.MemberAdmin/members-dashboard.js`

**Interfaces:**
- Consumes: `GET .../members` and `.../members/{key}` from Task 12.
- Produces: a "Medlemmar" dashboard in the Members section.

- [ ] **Step 1: Write the manifest**

Create `wwwroot/App_Plugins/NDSTK.MemberAdmin/umbraco-package.json`.

`Umb.Section.Members` is **plural** — verified against `Umbraco.Cms.StaticAssets` 18.1.1. `Umb.Section.Member` does not exist, and the failure mode is silent: the dashboard simply never renders.

```json
{
  "$schema": "../../../umbraco-package-schema.json",
  "id": "NDSTK.MemberAdmin",
  "name": "NDSTK Medlemsadministration",
  "version": "1.0.0",
  "extensions": [
    {
      "type": "dashboard",
      "alias": "NDSTK.MemberAdmin.Dashboard",
      "name": "Medlemmar",
      "element": "/App_Plugins/NDSTK.MemberAdmin/members-dashboard.js",
      "elementName": "ndstk-members-dashboard",
      "meta": { "label": "Medlemmar", "pathname": "ndstk-medlemmar", "weight": 100 },
      "conditions": [{ "alias": "Umb.Condition.SectionAlias", "match": "Umb.Section.Members" }]
    }
  ]
}
```

- [ ] **Step 2: Write the element**

Create `wwwroot/App_Plugins/NDSTK.MemberAdmin/members-dashboard.js`. Copy the import block and the `umbHttpClient` usage from
`c:\src\Esatto.Packages\Esatto.Umbraco.Backoffice.Redirects\wwwroot\App_Plugins\Esatto.Umbraco.Backoffice.Redirects\redirects-dashboard.js`:

```js
import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

const API_BASE = '/umbraco/management/api/v1/backoffice/ndstk/members';

// security must be declared explicitly - without it umbHttpClient does not attach the bearer
// token and every request 401s.
const SECURITY = [{ scheme: 'bearer', type: 'http' }];
```

The element holds `_rows`, `_search`, `_selected` and `_detail` as reactive state, and renders a `<uui-table>` with the spec's columns, a `<uui-input>` filter and an "Exportera CSV" button.

Filtering is client-side over name, email and child names. The member list is small enough that a round trip per keystroke would be strictly worse.

Three rendering rules that matter:

```js
// Öre to kronor happens only here, at the edge, the same way the server does it only in
// MembershipSettingsService.
const kr = (ore) => `${(ore / 100).toLocaleString('sv-SE')} kr`;

// A lapsed membership reads as a word, not as a negative number of days.
const daysLeft = (row) =>
    row.daysLeft === null ? '—' : row.daysLeft < 0 ? 'Utgången' : `${row.daysLeft} d`;

const date = (iso) => (iso ? new Date(iso).toLocaleDateString('sv-SE') : '—');
```

CSV export builds a Blob and clicks an object URL. Quote every field and double any embedded quote — a child's name can contain a comma. Prefix the content with `\ufeff` so Excel opens it as UTF-8 rather than mangling å, ä and ö.

- [ ] **Step 3: Verify at runtime**

Run the site and open **Members** in the backoffice. Expected: a "Medlemmar" tab listing every account with correct totals. Cross-check one row against the database. Type a child's name into the search and expect the parent's row. Export the CSV and open it — Swedish characters must survive.

- [ ] **Step 4: Commit**

```bash
git add wwwroot/App_Plugins
git commit -m "Add the Medlemmar backoffice dashboard"
```

---

### Task 14: The Deltagare workspace view

**Files:**
- Create: `wwwroot/App_Plugins/NDSTK.MemberAdmin/class-roster.js`
- Modify: `wwwroot/App_Plugins/NDSTK.MemberAdmin/umbraco-package.json`, `README.md`

**Interfaces:**
- Consumes: `GET .../members/roster/{classKey}` from Task 12.
- Produces: a "Deltagare" tab on every `trainingClass` node.

- [ ] **Step 1: Add the manifest entry**

Append to the `extensions` array. `Umb.Condition.WorkspaceContentTypeAlias` is verified present in 18.1.1:

```json
    {
      "type": "workspaceView",
      "alias": "NDSTK.MemberAdmin.ClassRoster",
      "name": "Deltagare",
      "element": "/App_Plugins/NDSTK.MemberAdmin/class-roster.js",
      "elementName": "ndstk-class-roster",
      "meta": { "label": "Deltagare", "pathname": "deltagare", "icon": "icon-users", "weight": 200 },
      "conditions": [
        { "alias": "Umb.Condition.WorkspaceAlias", "match": "Umb.Workspace.Document" },
        { "alias": "Umb.Condition.WorkspaceContentTypeAlias", "match": "trainingClass" }
      ]
    }
```

- [ ] **Step 2: Write the element**

Create `wwwroot/App_Plugins/NDSTK.MemberAdmin/class-roster.js`. It needs the class's key, which it takes from the document workspace context rather than from the URL:

```js
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';

    constructor() {
        super();
        this._rows = [];
        this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (context) => {
            // The workspace unique IS the content key, which is what ndstkBooking.ClassKey holds.
            this.observe(context.unique, (unique) => { if (unique) this.#load(unique); });
        });
    }
```

**Verify the context token and the observable's name against the running backoffice before assuming.** If `context.unique` is not an observable in 18.1.1, read it once inside the callback instead. A wrong guess here renders an empty tab with no error in the console, which is the hardest kind of failure to notice.

It renders the class name and "X av Y platser bokade" above a table of children with age, guardian name, email and phone, and the status in Swedish: *Bekräftad* / *Väntar på betalning*.

- [ ] **Step 3: Verify at runtime**

Open a class under **Träningar** in Content. Expect a "Deltagare" tab listing the booked children with their guardians' contact details. Book another child from the portal, reload, and expect them to appear. Open an article node and expect no such tab.

- [ ] **Step 4: Update the README**

Add a **Deltagare och familjekonton** section covering:

- The account holder is a guardian; participants attend. Even a solo account names one child, with a birth date.
- The family supplement's price, and that paying it does **not** move the membership expiry date — with the reason, since that is the non-obvious part.
- The welcome price is once per child, not once per account.
- The unique index is now `(ParticipantKey, ClassKey)`, which is what lets two siblings share a class, and it must stay in step with `Capacity.HasLiveBooking`.
- "Missade" means cancelled or unpaid, **not** absent: attendance is not recorded, so a no-show looks like an attendee.

Add `ndstkParticipant` to the Tables list, and a **Backoffice** section noting that the App_Plugins extension needs no npm and no bundler, and where the pattern came from.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/App_Plugins README.md
git commit -m "Add the class roster workspace view and document the feature"
```

---

## Self-review notes

**Spec coverage.** Every section of the spec maps to a task: participants → 3, 5; the index swap → 3, 4; membership and the family flag → 6, 7; Settings → 7; the `PaymentRecord` split → 3, 6; pricing → 1; migration and backfill → 3, 4; registration → 8; Mina barn → 9; the family upgrade → 10; booking → 6, 11; the management API → 12; the dashboard → 13; the workspace view → 14; what "missade" means → 12, 13, 14; README → 14. Credits deliberately need no task: they stay on `MemberKey` and nothing about them changes.

**Two things deliberately left to verify at implementation**, both flagged inline with a stated fallback rather than guessed: the declaring type of `EmailConfirmedDate` (Task 12), and the document workspace context's observable name (Task 14).

**Naming checked across tasks.** `ParticipantKey` throughout, never `ChildKey`. `FirstClassUsedUtc` on the record, `FirstClassUsed` on `ParticipantState`, `FirstClassAvailable` on the view row — three names because they are three different things: a timestamp, a rule input, and its negation for a view. `IsFamilyAccount` in C#, `familjekonto` as the Umbraco alias. `FamilyDueOre` on the quote but `FamilyFeeOre` on the price list and the payment record: the quote says what is *due now*, the others say what the fee *is*.
