using NDSTK.Booking.Domain;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Data;

/// <summary>
/// NPoco implementation of <see cref="IBookingRepository"/>, running inside an Umbraco scope so it
/// shares the ambient transaction and connection rather than opening its own.
/// </summary>
public sealed class BookingRepository(IScopeProvider scopeProvider) : IBookingRepository
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>>> GetBookingsByClassAsync(
        IReadOnlyCollection<Guid> classKeys)
    {
        if (classKeys.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<BookingSnapshot>>();
        }

        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        // One query for every class on the page rather than one per class: a portal listing twenty
        // classes would otherwise issue twenty round trips to render a single page.
        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .WhereIn<BookingRecord>(record => record.ClassKey, classKeys);

        List<BookingRecord> records = await scope.Database.FetchAsync<BookingRecord>(sql);

        return records
            .GroupBy(record => record.ClassKey)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BookingSnapshot>)[.. group.Select(ToSnapshot)]);
    }

    public async Task<IReadOnlyList<BookingSnapshot>> GetBookingsForMemberAsync(Guid memberKey)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record => record.MemberKey == memberKey)
            .OrderBy<BookingRecord>(record => record.ClassStartUtc);

        List<BookingRecord> records = await scope.Database.FetchAsync<BookingRecord>(sql);
        return [.. records.Select(ToSnapshot)];
    }

    public async Task<IReadOnlyList<CreditSnapshot>> GetCreditsForMemberAsync(Guid memberKey)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<CreditRecord>()
            .From<CreditRecord>()
            .Where<CreditRecord>(record => record.MemberKey == memberKey)
            .OrderBy<CreditRecord>(record => record.Id);

        List<CreditRecord> records = await scope.Database.FetchAsync<CreditRecord>(sql);
        return [.. records.Select(record => new CreditSnapshot(record.Id, record.MemberKey, record.SpentOnBookingId))];
    }

    // ----------------------------------------------------------------- writes

    public async Task<int?> TryReservePlaceAsync(
        Guid memberKey, Guid classKey, DateTime classStartUtc, int capacity,
        DateTime nowUtc, DateTime holdExpiresUtc)
    {
        if (capacity <= 0)
        {
            return null;
        }

        using IScope scope = scopeProvider.CreateScope();

        // One statement, so the capacity test and the insert cannot be separated by another
        // booking. Written as raw SQL because that atomicity is the entire point - the fluent
        // builder would produce a SELECT then an INSERT, and the gap between them is exactly the
        // overbooking window this is here to close.
        //
        // A place is taken by a confirmed booking, or by a pending one whose payment hold has not
        // yet run out. That must agree with Capacity.HoldsPlace, which is the same rule in C#.
        var inserted = await scope.Database.ExecuteAsync(
            $"""
            INSERT INTO {BookingTables.Booking}
                (MemberKey, ClassKey, ClassStartUtc, Status, CreatedUtc, HoldExpiresUtc)
            SELECT @0, @1, @2, @3, @4, @5
            WHERE (
                SELECT COUNT(*) FROM {BookingTables.Booking}
                WHERE ClassKey = @1
                  AND (Status = @6 OR (Status = @3 AND HoldExpiresUtc > @4))
            ) < @7
            """,
            memberKey, classKey, classStartUtc, Domain.BookingStatus.Pending,
            nowUtc, holdExpiresUtc, Domain.BookingStatus.Confirmed, capacity);

        if (inserted == 0)
        {
            // Full. Nothing was written, so there is nothing to roll back.
            scope.Complete();
            return null;
        }

        // Read the id back rather than using last_insert_rowid() or RETURNING, both of which are
        // provider specific. Inside this transaction the row is certainly visible.
        Sql<ISqlContext> findId = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record =>
                record.MemberKey == memberKey
                && record.ClassKey == classKey
                && record.Status == Domain.BookingStatus.Pending)
            .OrderByDescending<BookingRecord>(record => record.Id);

        BookingRecord? created = await scope.Database.FirstOrDefaultAsync<BookingRecord>(findId);

        scope.Complete();
        return created?.Id;
    }

    public async Task<bool> TrySpendCreditAsync(int creditId, int bookingId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // The "still unspent" test lives in the WHERE clause, so two bookings racing for the same
        // credit cannot both win: the second one updates zero rows.
        var updated = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Credit}
            SET SpentOnBookingId = @0, SpentUtc = @1
            WHERE Id = @2 AND SpentOnBookingId IS NULL
            """,
            bookingId, nowUtc, creditId);

        scope.Complete();
        return updated == 1;
    }

    public async Task<int> CreatePaymentAsync(PaymentRecord payment)
    {
        using IScope scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(payment);
        scope.Complete();
        return payment.Id;
    }

    public async Task LinkPaymentAsync(int bookingId, int paymentId)
    {
        using IScope scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {BookingTables.Booking} SET PaymentId = @0 WHERE Id = @1", paymentId, bookingId);
        scope.Complete();
    }

    public async Task<PaymentRecord?> GetPaymentByReferenceAsync(Guid reference)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<PaymentRecord>()
            .From<PaymentRecord>()
            .Where<PaymentRecord>(record => record.Reference == reference);

        return await scope.Database.FirstOrDefaultAsync<PaymentRecord>(sql);
    }

    public async Task<BookingRecord?> GetBookingAsync(int bookingId)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);
        return await scope.Database.SingleOrDefaultByIdAsync<BookingRecord>(bookingId);
    }

    public async Task ConfirmBookingAsync(int bookingId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // The hold is cleared as the booking is confirmed: a confirmed booking holds its place
        // outright, and leaving a stale expiry behind would let the sweeper release a paid place.
        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET Status = @0, ConfirmedUtc = @1, HoldExpiresUtc = NULL
            WHERE Id = @2
            """,
            Domain.BookingStatus.Confirmed, nowUtc, bookingId);

        scope.Complete();
    }

    public async Task CompletePaymentAsync(int paymentId, string status, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {BookingTables.Payment} SET Status = @0, CompletedUtc = @1 WHERE Id = @2",
            status, nowUtc, paymentId);
        scope.Complete();
    }

    public async Task ExpireBookingAsync(int bookingId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET Status = @0, HoldExpiresUtc = NULL
            WHERE Id = @1
            """,
            Domain.BookingStatus.Expired, bookingId);

        // Give back any credit that was spent on it. Without this a member who abandoned the Swish
        // page would silently lose a credit for a booking they never got.
        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Credit}
            SET SpentOnBookingId = NULL, SpentUtc = NULL
            WHERE SpentOnBookingId = @0
            """,
            bookingId);

        scope.Complete();
    }

    // -------------------------------------------------------- background job

    public async Task<IReadOnlyList<BookingRecord>> GetBookingsDueRemindersAsync(
        DateTime nowUtc, DateTime windowEndUtc)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        // Mirrors Reminders.Due, which is the same rule as a pure function. The index on
        // ClassStartUtc makes this a range scan rather than a walk over every booking ever made.
        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record =>
                record.Status == Domain.BookingStatus.Confirmed
                && record.ReminderSentUtc == null
                && record.ClassStartUtc > nowUtc
                && record.ClassStartUtc <= windowEndUtc)
            .OrderBy<BookingRecord>(record => record.ClassStartUtc);

        return await scope.Database.FetchAsync<BookingRecord>(sql);
    }

    public async Task<IReadOnlyList<BookingRecord>> GetExpiredHoldsAsync(DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record =>
                record.Status == Domain.BookingStatus.Pending
                && record.HoldExpiresUtc != null
                && record.HoldExpiresUtc <= nowUtc);

        return await scope.Database.FetchAsync<BookingRecord>(sql);
    }

    public async Task<bool> TryStampReminderSentAsync(int bookingId, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // Stamped before the mail is sent, and conditional on still being null. Two overlapping runs
        // therefore cannot both send: the loser updates zero rows and skips. The trade is that a
        // crash between the stamp and the send loses that one reminder - preferable to sending a
        // member the same reminder repeatedly.
        var stamped = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET ReminderSentUtc = @0
            WHERE Id = @1 AND ReminderSentUtc IS NULL
            """,
            nowUtc, bookingId);

        scope.Complete();
        return stamped == 1;
    }

    // ----------------------------------------------------- editor changes

    public async Task<int> ResyncClassStartAsync(Guid classKey, DateTime newStartUtc, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // Only live bookings. A cancelled booking is a historical record of a class as it was, and
        // rewriting its time would falsify that.
        //
        // ReminderSentUtc is cleared when the class moves to a later time, so the member gets a
        // fresh reminder for the new time - having been told "imorgon 18:00" for a class that has
        // since moved is worse than not having been told at all. Moving a class earlier does not
        // clear it, because the old reminder may already have been the more useful one.
        var affected = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET ClassStartUtc = @0,
                ReminderSentUtc = CASE WHEN @0 > ClassStartUtc THEN NULL ELSE ReminderSentUtc END
            WHERE ClassKey = @1
              AND Status IN (@2, @3)
              AND ClassStartUtc <> @0
            """,
            newStartUtc, classKey, Domain.BookingStatus.Confirmed, Domain.BookingStatus.Pending);

        scope.Complete();
        return affected;
    }

    public async Task<int> CancelAllForClassAsync(Guid classKey, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record =>
                record.ClassKey == classKey
                && (record.Status == Domain.BookingStatus.Confirmed
                    || record.Status == Domain.BookingStatus.Pending));

        List<BookingRecord> live = await scope.Database.FetchAsync<BookingRecord>(sql);

        var credited = 0;

        foreach (BookingRecord booking in live)
        {
            await scope.Database.ExecuteAsync(
                $"""
                UPDATE {BookingTables.Booking}
                SET Status = @0, CancelledUtc = @1, HoldExpiresUtc = NULL
                WHERE Id = @2
                """,
                Domain.BookingStatus.Cancelled, nowUtc, booking.Id);

            // Only a confirmed booking earns a credit. A pending one was never paid for, so there is
            // nothing to compensate - and issuing a credit for it would be free money.
            if (booking.Status != Domain.BookingStatus.Confirmed)
            {
                continue;
            }

            await scope.Database.InsertAsync(new CreditRecord
            {
                MemberKey = booking.MemberKey,
                SourceBookingId = booking.Id,
                IssuedUtc = nowUtc,
            });

            credited++;
        }

        scope.Complete();
        return credited;
    }

    public async Task<bool> TryCancelBookingAsync(int bookingId, Guid memberKey, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // Every precondition sits in the WHERE clause, which does four jobs at once: it stops a
        // member cancelling somebody else's booking, it stops a class being cancelled after it has
        // started, it stops a double submission minting a second credit, and it means the credit
        // below is only ever inserted by the caller that actually performed the cancellation.
        var cancelled = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET Status = @0, CancelledUtc = @1, HoldExpiresUtc = NULL
            WHERE Id = @2 AND MemberKey = @3 AND Status = @4 AND ClassStartUtc > @1
            """,
            Domain.BookingStatus.Cancelled, nowUtc, bookingId, memberKey,
            Domain.BookingStatus.Confirmed);

        if (cancelled != 1)
        {
            scope.Complete();
            return false;
        }

        // No refund, by design - the club keeps the money and the member keeps a place to use
        // later. A booking that was itself paid for with a credit still yields one back, so
        // cancelling never costs the member the credit they came in with.
        await scope.Database.InsertAsync(new CreditRecord
        {
            MemberKey = memberKey,
            SourceBookingId = bookingId,
            IssuedUtc = nowUtc,
        });

        scope.Complete();
        return true;
    }

    private static BookingSnapshot ToSnapshot(BookingRecord record) => new(
        record.Id,
        record.MemberKey,
        record.ClassKey,
        record.Status,
        record.HoldExpiresUtc,
        record.ClassStartUtc,
        record.ReminderSentUtc);
}
