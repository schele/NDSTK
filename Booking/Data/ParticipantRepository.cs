using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Data;

/// <summary>
/// NPoco implementation of <see cref="IParticipantRepository"/>, running inside an Umbraco scope so
/// it shares the ambient transaction and connection rather than opening its own.
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
            // Only the date carries meaning; the time is dropped rather than compared, the same
            // way MemberProfileService treats the membership expiry.
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

        // Ownership is a condition of the UPDATE, not a check before it: a forged key in a POST
        // then edits nothing, rather than racing a read that said it was fine.
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
