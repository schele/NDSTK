using System.Data.Common;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Data;

/// <summary>
/// NPoco implementation of <see cref="IBookingRepository"/>, running inside an Umbraco scope so it
/// shares the ambient transaction and connection rather than opening its own.
/// </summary>
public sealed class BookingRepository(
    IScopeProvider scopeProvider,
    ILogger<BookingRepository> logger)
    : IBookingRepository
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

    public async Task<bool> HasPaidFamilyFeeSinceAsync(Guid memberKey, DateTime sinceUtc)
    {
        using IScope scope = scopeProvider.CreateScope(autoComplete: true);

        // sinceUtc is passed as a parameter rather than formatted into the SQL. NPoco writes
        // datetimes as "yyyy-MM-dd HH:mm:ss.fffffff", and a hand-formatted round-trip ("o") sorts
        // differently as text - the same trap that once broke the reminder window.
        var count = await scope.Database.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM {BookingTables.Payment}
            WHERE MemberKey = @0 AND Status = @1 AND FamilyFeeOre > 0 AND CompletedUtc >= @2
            """,
            memberKey, PaymentStatus.Paid, sinceUtc);

        return count > 0;
    }

    // ----------------------------------------------------------------- writes

    public async Task<int?> TryReservePlaceAsync(
        Guid memberKey, Guid participantKey, Guid classKey, DateTime classStartUtc, int capacity,
        DateTime nowUtc, DateTime holdExpiresUtc)
    {
        if (capacity <= 0)
        {
            return null;
        }

        using IScope scope = scopeProvider.CreateScope();

        // First, retire this child's own expired hold on this class, if they have one.
        //
        // Without this the two rules disagree and the insert below throws. The partial unique index
        // treats every Pending row as live, because an index predicate cannot reference "now";
        // Capacity.HoldsPlace treats a Pending row as live only until its hold runs out. So a member
        // who abandoned a payment and came back to book the same class again passed the C# check and
        // then hit "UNIQUE constraint failed" - a 500 for something entirely reasonable to do.
        //
        // Keyed on the participant, matching the index: cleaning up by account would retire a
        // *sibling's* live hold on the same class, which is exactly the case a family account makes
        // routine.
        //
        // Any credit spent on that abandoned booking goes back first: the member never got the place
        // it was spent on.
        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Credit}
            SET SpentOnBookingId = NULL, SpentUtc = NULL
            WHERE SpentOnBookingId IN (
                SELECT Id FROM {BookingTables.Booking}
                WHERE ParticipantKey = @0 AND ClassKey = @1 AND Status = @2 AND HoldExpiresUtc <= @3)
            """,
            participantKey, classKey, Domain.BookingStatus.Pending, nowUtc);

        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET Status = @0, HoldExpiresUtc = NULL
            WHERE ParticipantKey = @1 AND ClassKey = @2 AND Status = @3 AND HoldExpiresUtc <= @4
            """,
            Domain.BookingStatus.Expired, participantKey, classKey, Domain.BookingStatus.Pending, nowUtc);

        // One statement, so the capacity test and the insert cannot be separated by another
        // booking. Written as raw SQL because that atomicity is the entire point - the fluent
        // builder would produce a SELECT then an INSERT, and the gap between them is exactly the
        // overbooking window this is here to close.
        //
        // A place is taken by a confirmed booking, or by a pending one whose payment hold has not
        // yet run out. That must agree with Capacity.HoldsPlace, which is the same rule in C#.
        int inserted;

        try
        {
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
                // Passed as an explicit array: NPoco's ExecuteAsync overloads stop at eight
                // parameters, and this statement now needs nine.
                [
                    memberKey, participantKey, classKey, classStartUtc, Domain.BookingStatus.Pending,
                    nowUtc, holdExpiresUtc, Domain.BookingStatus.Confirmed, capacity,
                ]);
        }
        catch (DbException exception)
            when (exception.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            // The one-live-booking-per-class index fired. With the stale-hold cleanup above this
            // should only happen when the same member submits twice at once - a double-click, or a
            // resubmitted form - so it is a normal outcome rather than a fault, and the caller
            // reports "du är redan bokad".
            //
            // Caught deliberately: a database constraint is a backstop, and a backstop that reaches
            // the member as a 500 has failed at its job. Logged at warning so a genuine divergence
            // between the index and the C# rule is still visible rather than silently swallowed.
            logger.LogWarning(
                "A duplicate booking for participant {ParticipantKey} on class {ClassKey} was "
                + "rejected by the one-live-booking index.", participantKey, classKey);

            scope.Complete();
            return null;
        }

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
                record.ParticipantKey == participantKey
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

    public async Task<bool> TryCancelBookingAsync(
        int bookingId, Guid memberKey, DateTime nowUtc, DateTime earliestCancellableStartUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        // Every precondition sits in the WHERE clause, which does five jobs at once: it stops a
        // member cancelling somebody else's booking, it stops a class being cancelled once it is
        // inside the cancellation deadline (and so also after it has started), it stops a double
        // submission minting a second credit, and it means the credit below is only ever inserted
        // by the caller that actually performed the cancellation.
        //
        // The deadline arrives as an absolute moment rather than a number of hours, because SQL
        // cannot add hours to "now" portably - and computing it once, in the service, is what keeps
        // the SQL and Cancellation.IsOpen describing the same boundary.
        var cancelled = await scope.Database.ExecuteAsync(
            $"""
            UPDATE {BookingTables.Booking}
            SET Status = @0, CancelledUtc = @1, HoldExpiresUtc = NULL
            WHERE Id = @2 AND MemberKey = @3 AND Status = @4 AND ClassStartUtc > @5
            """,
            Domain.BookingStatus.Cancelled, nowUtc, bookingId, memberKey,
            Domain.BookingStatus.Confirmed, earliestCancellableStartUtc);

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

    /// <remarks>
    /// A null ParticipantKey maps to <see cref="Guid.Empty"/>, which matches no participant. That
    /// can only happen if the backfill refused to complete, and failing closed - the child looks
    /// unbooked and the rules turn them away - is the safe direction.
    /// </remarks>
    public async Task<(int Cancelled, int Credited)> CancelFutureBookingsForParticipantAsync(
        Guid participantKey, Guid memberKey, DateTime nowUtc)
    {
        using IScope scope = scopeProvider.CreateScope();

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<BookingRecord>()
            .From<BookingRecord>()
            .Where<BookingRecord>(record =>
                record.ParticipantKey == participantKey
                && record.MemberKey == memberKey
                && record.ClassStartUtc > nowUtc
                && (record.Status == Domain.BookingStatus.Confirmed
                    || record.Status == Domain.BookingStatus.Pending));

        List<BookingRecord> live = await scope.Database.FetchAsync<BookingRecord>(sql);

        var cancelled = 0;
        var credited = 0;

        foreach (BookingRecord booking in live)
        {
            // Conditional on the status not having moved since the read, so a cancellation racing
            // this one cannot end up crediting the same booking twice.
            var affected = await scope.Database.ExecuteAsync(
                $"""
                UPDATE {BookingTables.Booking}
                SET Status = @0, CancelledUtc = @1, HoldExpiresUtc = NULL
                WHERE Id = @2 AND Status = @3
                """,
                Domain.BookingStatus.Cancelled, nowUtc, booking.Id, booking.Status);

            if (affected == 0)
            {
                continue;
            }

            cancelled++;

            // Only a confirmed booking earns a credit. A pending one was never paid for, so there
            // is nothing to compensate - and crediting it would be free money.
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
        return (cancelled, credited);
    }

    private static BookingSnapshot ToSnapshot(BookingRecord record) => new(
        record.Id,
        record.MemberKey,
        record.ParticipantKey ?? Guid.Empty,
        record.ClassKey,
        record.Status,
        record.HoldExpiresUtc,
        record.ClassStartUtc,
        record.ReminderSentUtc);
}
