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

    private static BookingSnapshot ToSnapshot(BookingRecord record) => new(
        record.Id,
        record.MemberKey,
        record.ClassKey,
        record.Status,
        record.HoldExpiresUtc,
        record.ClassStartUtc,
        record.ReminderSentUtc);
}
