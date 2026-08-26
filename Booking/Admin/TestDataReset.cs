using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Services;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace NDSTK.Booking.Admin;

/// <summary>
/// What a reset removed, so the backoffice can say so rather than claiming success silently.
/// </summary>
public sealed record TestDataResetResult(
    int Bookings, int Payments, int Credits, int Participants, int Members);

/// <summary>
/// Puts accounts back to the state a brand-new member is in, for walking the booking flow again
/// from the start.
/// </summary>
/// <remarks>
/// This exists because doing it by hand is slow and easy to get wrong. Editing the database
/// directly means stopping the site first - Umbraco caches member properties in process, so a
/// membership cleared underneath a running site stays valid until the next restart. Going through
/// <see cref="IMemberService"/> instead lets Umbraco invalidate its own caches, which is what makes
/// this safe to click while the site is up.
///
/// Deliberately does not delete the members themselves. The login and the verified address are the
/// slow part to recreate, and testing the booking flow does not need them gone. Registration is
/// tested with a fresh address instead.
///
/// It is destructive and unauthenticated code has no business near it - see
/// <see cref="TestDataResetGate"/> for what stands in front of it.
/// </remarks>
public sealed class TestDataReset(
    IScopeProvider scopeProvider,
    IMemberService memberService,
    MemberProfileService profiles,
    ILogger<TestDataReset> logger)
{
    /// <summary>Clears every account's bookings, payments, credits, children and membership.</summary>
    public Task<TestDataResetResult> ResetEverythingAsync() => ResetAsync(memberKey: null);

    /// <summary>Clears one account, leaving every other account and their places untouched.</summary>
    public Task<TestDataResetResult> ResetMemberAsync(Guid memberKey) => ResetAsync(memberKey);

    private async Task<TestDataResetResult> ResetAsync(Guid? memberKey)
    {
        // One scope for all four deletes: a reset that emptied the bookings and then failed would
        // leave places held by children who no longer exist, which is worse than not starting.
        using IScope scope = scopeProvider.CreateScope();

        // Credits and payments reference bookings, and bookings reference participants, so they go
        // in that order. Nothing here relies on SQLite enforcing it - foreign keys are off by
        // default - but a delete order that matches the references survives turning them on.
        var credits = await DeleteAsync(scope, BookingTables.Credit, memberKey);
        var payments = await DeleteAsync(scope, BookingTables.Payment, memberKey);
        var bookings = await DeleteAsync(scope, BookingTables.Booking, memberKey);
        var participants = await DeleteAsync(scope, BookingTables.Participant, memberKey);

        scope.Complete();

        // Outside the scope on purpose. IMemberService runs in a scope of its own, and the
        // membership values are not part of the same consistency question: an account with its
        // bookings gone and its membership still paid is odd but harmless, and it is visible.
        var members = await ClearMembershipsAsync(memberKey);

        var result = new TestDataResetResult(bookings, payments, credits, participants, members);

        logger.LogWarning(
            "Test data reset{Scope}: {Bookings} booking(s), {Payments} payment(s), {Credits} " +
            "credit(s), {Participants} child(ren) and {Members} membership(s) cleared.",
            memberKey is null ? string.Empty : $" for {memberKey}",
            bookings, payments, credits, participants, members);

        return result;
    }

    /// <remarks>
    /// Logged at warning level rather than information: this throws data away, and the line that
    /// says so should stand out in a log somebody is reading for another reason entirely.
    /// </remarks>
    private static Task<int> DeleteAsync(IScope scope, string table, Guid? memberKey)
        => memberKey is { } key
            // The arguments go in an explicit array: NPoco's ExecuteAsync has a CancellationToken
            // overload, and a lone Guid binds to that one instead of to the params array.
            ? scope.Database.ExecuteAsync($"DELETE FROM {table} WHERE MemberKey = @0", [key])
            : scope.Database.ExecuteAsync($"DELETE FROM {table}");

    private async Task<int> ClearMembershipsAsync(Guid? memberKey)
    {
        if (memberKey is { } key)
        {
            return await profiles.ClearMembershipAsync(key) ? 1 : 0;
        }

        // GetAllMembers with no type filter is the whole member set. A test site has a handful, and
        // the alternative - paging - would buy nothing on a call that is about to write to each of
        // them one at a time anyway.
        var cleared = 0;
        foreach (Guid key2 in memberService.GetAllMembers().Select(member => member.Key))
        {
            if (await profiles.ClearMembershipAsync(key2))
            {
                cleared++;
            }
        }

        return cleared;
    }
}
