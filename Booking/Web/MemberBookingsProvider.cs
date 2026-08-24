using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Services;

namespace NDSTK.Booking.Web;

/// <summary>
/// What the shared "Mina bokningar" partial renders.
/// </summary>
/// <param name="AllowCancel">
/// False on the payment page. Cancelling one booking while part-way through paying for another is a
/// confusing thing to offer: the member is mid-transaction, and the two bookings are unrelated. The
/// action stays available on the portal, which is where managing bookings belongs.
/// </param>
public sealed record MemberBookingsPanel(
    IReadOnlyList<MemberBookingRow> Rows,
    bool AllowCancel);

/// <summary>
/// Builds the "Mina bokningar" rows for one member.
/// </summary>
/// <remarks>
/// Shared by the portal and the payment page, which both show the box. Keeping it in one place is
/// what stops the two drifting into showing different sets of bookings.
/// </remarks>
public sealed class MemberBookingsProvider(
    IBookingRepository repository,
    IParticipantRepository participants,
    TrainingClassService classes)
{
    /// <summary>
    /// The member's current bookings - confirmed, and not yet started - soonest first.
    /// </summary>
    /// <remarks>
    /// Only current ones. A cancelled booking is not a booking any more, an expired hold is
    /// bookkeeping for a payment nobody completed, and a class that has already happened is history
    /// rather than something the member still needs to act on.
    ///
    /// Sorted soonest first, unlike a history list: the next thing to turn up for is the thing
    /// worth reading at the top.
    /// </remarks>
    public async Task<IReadOnlyList<MemberBookingRow>> GetCurrentAsync(Guid memberKey)
    {
        IReadOnlyList<BookingSnapshot> snapshots = await repository.GetBookingsForMemberAsync(memberKey);
        IReadOnlyList<CreditSnapshot> credits = await repository.GetCreditsForMemberAsync(memberKey);

        // Which child each booking is for. On a family account the list is otherwise ambiguous -
        // two identical rows for the same class, and no way to tell whose place is whose.
        // Removed children are included deliberately: their past bookings still need a name.
        Dictionary<Guid, string> childNames = (await participants.GetAllForMemberAsync(memberKey))
            .ToDictionary(child => child.Key, child => $"{child.FirstName} {child.LastName}".Trim());

        HashSet<int> paidByCredit =
        [
            .. credits
                .Where(credit => credit.SpentOnBookingId is not null)
                .Select(credit => credit.SpentOnBookingId!.Value),
        ];

        DateTime nowUtc = DateTime.UtcNow;

        return
        [
            .. snapshots
                .Where(snapshot => snapshot.Status == BookingStatus.Confirmed)
                .Where(snapshot => snapshot.ClassStartUtc > nowUtc)
                .OrderBy(snapshot => snapshot.ClassStartUtc)
                .Select(snapshot => new MemberBookingRow(
                    snapshot.Id,
                    // Null when an editor has deleted the class. The row still renders, because the
                    // booking carries its own copy of the start time - a member who paid deserves to
                    // see it either way.
                    classes.Find(snapshot.ClassKey),
                    childNames.TryGetValue(snapshot.ParticipantKey, out var name) ? name : string.Empty,
                    snapshot.Status,
                    snapshot.ClassStartUtc,
                    UsedCredit: paidByCredit.Contains(snapshot.Id))),
        ];
    }
}
