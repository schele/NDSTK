using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Services;
using NDSTK.Booking.Web;
using NPoco;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Admin;

/// <summary>
/// Every read the backoffice needs about members, payments and rosters.
/// </summary>
/// <remarks>
/// Kept out of <see cref="IBookingRepository"/> on purpose: that interface is the booking path, and
/// growing it a reporting surface would mean the rules and the reports share a contract only one of
/// them needs.
///
/// The counts come from grouped queries joined in memory rather than one query per member. A club
/// with two hundred members must not issue two hundred round trips to render one table.
/// </remarks>
public sealed class MemberAdminQueries(
    IScopeProvider scopeProvider,
    IMemberService memberService,
    TrainingClassService classes)
{
    public async Task<IReadOnlyList<MemberAdminRow>> GetMembersAsync()
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Dictionary<Guid, BookingCounts> counts = (await scope.Database.FetchAsync<BookingCounts>(
            $"""
            SELECT MemberKey,
                   SUM(CASE WHEN Status = @0 THEN 1 ELSE 0 END) AS Confirmed,
                   SUM(CASE WHEN Status = @1 THEN 1 ELSE 0 END) AS Cancelled
            FROM {BookingTables.Booking}
            GROUP BY MemberKey
            """,
            BookingStatus.Confirmed, BookingStatus.Cancelled))
            .ToDictionary(row => row.MemberKey);

        Dictionary<Guid, PaymentTotals> totals = (await scope.Database.FetchAsync<PaymentTotals>(
            $"""
            SELECT MemberKey,
                   SUM(AmountOre) AS TotalPaidOre,
                   MAX(CompletedUtc) AS LastPaymentUtc,
                   MIN(CASE WHEN MembershipFeeOre > 0 THEN CompletedUtc END) AS MemberSinceUtc
            FROM {BookingTables.Payment}
            WHERE Status = @0
            GROUP BY MemberKey
            """,
            PaymentStatus.Paid))
            .ToDictionary(row => row.MemberKey);

        Dictionary<Guid, int> credits = (await scope.Database.FetchAsync<CreditCount>(
            $"""
            SELECT MemberKey, COUNT(*) AS Unspent
            FROM {BookingTables.Credit}
            WHERE SpentOnBookingId IS NULL
            GROUP BY MemberKey
            """))
            .ToDictionary(row => row.MemberKey, row => row.Unspent);

        Dictionary<Guid, List<string>> children = (await scope.Database.FetchAsync<ChildName>(
            $"""
            SELECT MemberKey, FirstName, LastName
            FROM {BookingTables.Participant}
            WHERE RemovedUtc IS NULL
            ORDER BY Id
            """))
            .GroupBy(row => row.MemberKey)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => $"{row.FirstName} {row.LastName}".Trim()).ToList());

        return
        [
            .. memberService.GetAllMembers()
                .Select(member => ToRow(member, counts, totals, credits, children))
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    public async Task<MemberAdminDetail?> GetDetailAsync(Guid memberKey)
    {
        MemberAdminRow? summary = (await GetMembersAsync())
            .FirstOrDefault(row => row.MemberKey == memberKey);

        if (summary is null)
        {
            return null;
        }

        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        List<AdminPaymentRow> payments = await scope.Database.FetchAsync<AdminPaymentRow>(
            $"""
            SELECT CreatedUtc, CompletedUtc, AmountOre, MembershipFeeOre, FamilyFeeOre,
                   ClassFeeOre, Status, Provider, BankReference, ErrorCode
            FROM {BookingTables.Payment}
            WHERE MemberKey = @0
            ORDER BY Id DESC
            """,
            memberKey);

        List<BookingWithChild> rows = await scope.Database.FetchAsync<BookingWithChild>(
            $"""
            SELECT b.ClassKey, b.ClassStartUtc, b.Status, p.FirstName, p.LastName
            FROM {BookingTables.Booking} b
            LEFT JOIN {BookingTables.Participant} p ON p.Key = b.ParticipantKey
            WHERE b.MemberKey = @0
            ORDER BY b.ClassStartUtc DESC
            """,
            memberKey);

        IReadOnlyList<AdminBookingRow> bookings =
        [
            .. rows.Select(row => new AdminBookingRow(
                $"{row.FirstName} {row.LastName}".Trim(),
                // Null when an editor has deleted the class. The booking still has to render: the
                // member paid for it, and it is part of what the club took money for.
                classes.Find(row.ClassKey)?.Title ?? "Borttagen träning",
                row.ClassStartUtc,
                row.Status)),
        ];

        return new MemberAdminDetail(summary, payments, bookings);
    }

    /// <summary>
    /// Who is on one class. Pending counts as booked: a place being held for an unpaid booking is
    /// still a place taken, which is what <see cref="Capacity.HoldsPlace"/> says too.
    /// </summary>
    public async Task<IReadOnlyList<ClassRosterRow>> GetRosterAsync(Guid classKey)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        List<RosterRecord> rows = await scope.Database.FetchAsync<RosterRecord>(
            $"""
            SELECT b.Id, b.Status, b.CreatedUtc, b.MemberKey,
                   p.FirstName, p.LastName, p.BirthDate
            FROM {BookingTables.Booking} b
            JOIN {BookingTables.Participant} p ON p.Key = b.ParticipantKey
            WHERE b.ClassKey = @0 AND b.Status IN (@1, @2)
            ORDER BY p.FirstName, p.LastName
            """,
            classKey, BookingStatus.Confirmed, BookingStatus.Pending);

        if (rows.Count == 0)
        {
            return [];
        }

        // What each place on this class was paid, and which were paid with a credit. Two grouped
        // queries scoped by the class rather than a lookup per row.
        // The CLASS fee, not the payment total. A payment legitimately carries the annual fee and
        // the family supplement alongside it - the first booking of a membership year comes to 350
        // when the class itself was 100 - and in a column headed "Betalning" on one class's roster
        // that reads as the price of the class. The whole payment, split three ways, is on the
        // member's row in the Medlemmar dashboard, which is where a question about fees belongs.
        //
        // Zero is filtered out rather than stored: a place covered by a credit has no class fee, and
        // "0 kr" says less than the credit label that replaces it.
        Dictionary<int, int> paid = (await scope.Database.FetchAsync<PaidBooking>(
            $"""
            SELECT p.BookingId AS BookingId, SUM(p.ClassFeeOre) AS PaidOre
            FROM {BookingTables.Payment} p
            JOIN {BookingTables.Booking} b ON b.Id = p.BookingId
            WHERE b.ClassKey = @0 AND p.Status = @1
            GROUP BY p.BookingId
            HAVING SUM(p.ClassFeeOre) > 0
            """,
            classKey, PaymentStatus.Paid))
            .ToDictionary(row => row.BookingId, row => row.PaidOre);

        // Mapped through a record rather than FetchAsync<int>. NPoco does handle a scalar column,
        // but this endpoint needs backoffice auth to exercise, so the version that cannot surprise
        // anybody is the one worth shipping.
        HashSet<int> creditPaid =
        [
            .. (await scope.Database.FetchAsync<CreditedBooking>(
                $"""
                SELECT c.SpentOnBookingId AS BookingId
                FROM {BookingTables.Credit} c
                JOIN {BookingTables.Booking} b ON b.Id = c.SpentOnBookingId
                WHERE b.ClassKey = @0
                """,
                classKey))
                .Select(row => row.BookingId),
        ];

        // One lookup covering every guardian on the class, rather than one per row.
        Dictionary<Guid, IMember> guardians =
            (await memberService.GetByKeysAsync([.. rows.Select(row => row.MemberKey).Distinct()]))
            .ToDictionary(member => member.Key);

        DateOnly classDay = DateOnly.FromDateTime(
            SwedishTime.ToSwedish(classes.Find(classKey)?.StartUtc ?? DateTime.UtcNow));

        return
        [
            .. rows.Select(row =>
            {
                guardians.TryGetValue(row.MemberKey, out IMember? guardian);

                return new ClassRosterRow(
                    row.Id,
                    $"{row.FirstName} {row.LastName}".Trim(),
                    // Aged on the day the class runs rather than today, so an autumn roster does
                    // not age a child by a year halfway through the term.
                    row.BirthDate is { } born
                        ? SwedishDate.AgeOn(DateOnly.FromDateTime(born), classDay)
                        : null,
                    guardian?.Name ?? string.Empty,
                    guardian?.Email ?? string.Empty,
                    guardian?.GetValue<string>(MemberProfileService.PhoneAlias),
                    row.Status,
                    paid.TryGetValue(row.Id, out var amount) ? amount : null,
                    creditPaid.Contains(row.Id),
                    row.CreatedUtc);
            }),
        ];
    }

    private static MemberAdminRow ToRow(
        IMember member,
        IReadOnlyDictionary<Guid, BookingCounts> counts,
        IReadOnlyDictionary<Guid, PaymentTotals> totals,
        IReadOnlyDictionary<Guid, int> credits,
        IReadOnlyDictionary<Guid, List<string>> children)
    {
        counts.TryGetValue(member.Key, out BookingCounts? count);
        totals.TryGetValue(member.Key, out PaymentTotals? total);
        children.TryGetValue(member.Key, out List<string>? names);

        var paidUntil = member.GetValue<DateTime?>(MemberProfileService.MembershipPaidUntilAlias);

        return new MemberAdminRow(
            member.Key,
            // Members registered before the guardian's name was collected still show as an email.
            member.Name ?? member.Email,
            member.Email,
            member.GetValue<string>(MemberProfileService.PhoneAlias),
            member.GetValue<bool>(MemberProfileService.FamilyAccountAlias),
            member.EmailConfirmedDate,
            // The first payment that included the årsavgift is when somebody became a member. A
            // comped membership has no such payment, so fall back to when the account was made -
            // but only where a membership actually exists.
            //
            // Without that guard the fallback fired for every account, so one that registered and
            // never confirmed its address showed a "member since" date. Registration creates the
            // member unapproved and only verification approves it, so such an account cannot even
            // sign in - it is a dead registration, and this column was calling it a membership.
            total?.MemberSinceUtc ?? (paidUntil is null ? null : member.CreateDate),
            paidUntil is null ? null : DateOnly.FromDateTime(paidUntil.Value),
            total?.TotalPaidOre ?? 0,
            total?.LastPaymentUtc,
            names?.Count ?? 0,
            count?.Confirmed ?? 0,
            count?.Cancelled ?? 0,
            credits.TryGetValue(member.Key, out var unspent) ? unspent : 0,
            names ?? []);
    }

    // The grouped-query shapes. Private because nothing outside this file should depend on the
    // exact columns; the records in the sibling files are the contract.

    private sealed class BookingCounts
    {
        public Guid MemberKey { get; set; }
        public int Confirmed { get; set; }
        public int Cancelled { get; set; }
    }

    private sealed class PaymentTotals
    {
        public Guid MemberKey { get; set; }
        public int TotalPaidOre { get; set; }
        public DateTime? LastPaymentUtc { get; set; }
        public DateTime? MemberSinceUtc { get; set; }
    }

    private sealed class CreditCount
    {
        public Guid MemberKey { get; set; }
        public int Unspent { get; set; }
    }

    private sealed class ChildName
    {
        public Guid MemberKey { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    private sealed class BookingWithChild
    {
        public Guid ClassKey { get; set; }
        public DateTime ClassStartUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    private sealed class PaidBooking
    {
        public int BookingId { get; set; }
        public int PaidOre { get; set; }
    }

    private sealed class CreditedBooking
    {
        public int BookingId { get; set; }
    }

    private sealed class RosterRecord
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public Guid MemberKey { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
    }
}
