namespace NDSTK.Booking.Domain;

/// <summary>
/// A class as one particular member sees it: how many places are left, whether they already hold
/// one, and therefore whether the portal should offer a booking button.
/// </summary>
public sealed record BookableClass(
    TrainingClass Class,
    int RemainingPlaces,
    bool MemberHasBooking,
    bool HasStarted)
{
    public bool IsFull => RemainingPlaces <= 0;

    /// <summary>
    /// Every reason a booking button should be hidden, in one place, so the view cannot forget one.
    /// </summary>
    public bool CanBook => HasStarted is false && IsFull is false && MemberHasBooking is false;

    /// <summary>
    /// Projects a class for a member. Pure, so the interesting combinations - full, already booked,
    /// already started, no capacity set - are all cheap to test.
    /// </summary>
    /// <param name="memberKey">Null for an anonymous visitor, who never "has" a booking.</param>
    public static BookableClass From(
        TrainingClass trainingClass,
        IEnumerable<BookingSnapshot> bookings,
        Guid? memberKey,
        DateTime nowUtc)
    {
        BookingSnapshot[] forThisClass = [.. bookings];

        // A capacity an editor never filled in reads as zero, and zero must mean "not bookable"
        // rather than "unlimited" - the safe direction to fail in is turning people away.
        var remaining = Capacity.RemainingPlaces(trainingClass.Capacity, forThisClass, nowUtc);

        var memberHasBooking = memberKey is { } key
                               && Domain.Capacity.HasLiveBooking(forThisClass, key, nowUtc);

        return new BookableClass(
            trainingClass,
            remaining,
            memberHasBooking,
            HasStarted: trainingClass.StartUtc <= nowUtc);
    }
}
