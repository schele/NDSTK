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
/// the old, narrower rule, so at no point is there a window with no index.
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

    /// <summary>The retired per-account welcome flag, read once here and never written again.</summary>
    private const string LegacyDiscountAlias = "firstClassDiscountUsed";

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
        var swapped = SwapIndex(scope);

        scope.Complete();

        // Only marked done if the index actually swapped. Otherwise the next boot tries again,
        // which is what should happen: the site is running on the old, narrower guarantee.
        if (swapped)
        {
            keyValueService.SetValue(StateKey, StateValue);
        }

        logger.LogInformation(
            "Participant backfill: {Created} participants created, {Pointed} bookings repointed, "
            + "index swapped: {Swapped}.",
            created, pointed, swapped);
    }

    /// <summary>
    /// One participant per member that has none. The name comes from the email's local part, which
    /// is a guess - so the birth date is left null, and the portal makes the member correct both
    /// before they can book again. Inventing a birth date would be worse than asking for it once.
    /// </summary>
    private int CreateMissingParticipants(IScope scope)
    {
        var created = 0;
        DateTime nowUtc = DateTime.UtcNow;

        foreach (IMember member in memberService.GetAllMembers())
        {
            var exists = scope.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {BookingTables.Participant} WHERE MemberKey = @0",
                member.Key) > 0;

            if (exists)
            {
                continue;
            }

            var localPart = member.Email?.Split('@')[0];

            scope.Database.Insert(new ParticipantRecord
            {
                Key = Guid.NewGuid(),
                MemberKey = member.Key,
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "Deltagare" : localPart,
                LastName = string.Empty,
                BirthDate = null,
                CreatedUtc = nowUtc,
            });

            created++;
        }

        return created;
    }

    /// <summary>
    /// Every existing booking belonged to an account that had exactly one participant a moment ago,
    /// so the oldest participant on the account is unambiguously the right one.
    /// </summary>
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
    /// charged; only whether the column is null is ever read, so an approximate date is harmless.
    /// </summary>
    private void StampSpentWelcomePrices(IScope scope)
    {
        foreach (IMember member in memberService.GetAllMembers())
        {
            if (member.GetValue<bool>(LegacyDiscountAlias) is false)
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

    private bool SwapIndex(IScope scope)
    {
        var unpointed = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {BookingTables.Booking} WHERE ParticipantKey IS NULL");

        if (unpointed > 0)
        {
            // Refuse rather than build an index that enforces nothing. Leaving the old index in
            // place keeps the old, narrower guarantee until this is investigated.
            logger.LogError(
                "{Count} bookings still have no ParticipantKey, so the one-live-booking index was "
                + "left keyed on the account. Two siblings cannot yet share a class.",
                unpointed);

            return false;
        }

        scope.Database.Execute($"DROP INDEX IF EXISTS {OldIndex}");
        scope.Database.Execute(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS {NewIndex}
            ON {BookingTables.Booking} (ParticipantKey, ClassKey)
            WHERE Status IN ('{Domain.BookingStatus.Pending}', '{Domain.BookingStatus.Confirmed}')
            """);

        return true;
    }
}
