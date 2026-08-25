namespace NDSTK.Booking.Domain;

/// <summary>
/// A class as one particular account sees it: how many places are left, which of that account's
/// children already hold one, and therefore whether the portal should offer a booking button.
/// </summary>
/// <param name="BookedParticipantKeys">
/// The account's children who already hold a live place on this class.
/// </param>
/// <param name="BookableParticipantKeys">
/// The account's children who do not, and could still be booked onto it. On a family account a
/// class is routinely bookable for one child and not another, which is why these are sets rather
/// than a single flag.
/// </param>
public sealed record BookableClass(
    TrainingClass Class,
    int RemainingPlaces,
    IReadOnlySet<Guid> BookedParticipantKeys,
    IReadOnlyList<Guid> BookableParticipantKeys,
    bool HasStarted)
{
    public bool IsFull => RemainingPlaces <= 0;

    /// <summary>True when at least one of the account's children is already on this class.</summary>
    public bool MemberHasBooking => BookedParticipantKeys.Count > 0;

    /// <summary>
    /// Every one of the account's children is already on this class, so it has nothing left to
    /// offer them and does not belong in a list headed "Boka träning".
    /// </summary>
    /// <remarks>
    /// A family with one of two children booked is not finished with the class - the other can
    /// still be booked onto it - so this is deliberately not the same as
    /// <see cref="MemberHasBooking"/>.
    ///
    /// An anonymous visitor never satisfies it: with no children, nothing is booked, and the list
    /// stays a full shop window.
    /// </remarks>
    public bool EveryChildBooked =>
        BookedParticipantKeys.Count > 0 && BookableParticipantKeys.Count == 0;

    /// <summary>
    /// Every reason a booking button should be hidden, in one place, so the view cannot forget one.
    /// </summary>
    /// <remarks>
    /// An account with no children at all is an anonymous visitor: both sets are empty, and the
    /// button stays visible so they are prompted to sign in rather than shown a class that looks
    /// unavailable. A signed-in account whose every child is already booked has an empty bookable
    /// set but a non-empty booked one, which is what tells the two cases apart.
    /// </remarks>
    public bool CanBook =>
        HasStarted is false
        && IsFull is false
        && (BookedParticipantKeys.Count == 0 || BookableParticipantKeys.Count > 0);

    /// <summary>
    /// Projects a class for an account. Pure, so the interesting combinations - full, one child
    /// booked, every child booked, already started, no capacity set - are all cheap to test.
    /// </summary>
    /// <param name="participantKeys">
    /// The account's live children. Empty for an anonymous visitor, who never "has" a booking.
    /// </param>
    public static BookableClass From(
        TrainingClass trainingClass,
        IEnumerable<BookingSnapshot> bookings,
        IReadOnlyCollection<Guid> participantKeys,
        DateTime nowUtc)
    {
        BookingSnapshot[] forThisClass = [.. bookings];

        // A capacity an editor never filled in reads as zero, and zero must mean "not bookable"
        // rather than "unlimited" - the safe direction to fail in is turning people away.
        var remaining = Capacity.RemainingPlaces(trainingClass.Capacity, forThisClass, nowUtc);

        HashSet<Guid> booked =
        [
            .. participantKeys.Where(key => Capacity.HasLiveBooking(forThisClass, key, nowUtc)),
        ];

        return new BookableClass(
            trainingClass,
            remaining,
            booked,
            [.. participantKeys.Where(key => booked.Contains(key) is false)],
            HasStarted: trainingClass.StartUtc <= nowUtc);
    }
}
