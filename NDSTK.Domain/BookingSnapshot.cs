namespace NDSTK.Booking.Domain;

/// <summary>
/// Just enough of a booking for the pure rules to work with, so capacity, reminders and the
/// one-live-booking check need no database.
/// </summary>
/// <param name="ClassStartUtc">
/// The booking's own copy of the class start time. Carried here so a booking still renders and
/// still reminds correctly even if the class node is later deleted.
/// </param>
public sealed record BookingSnapshot(
    int Id,
    Guid MemberKey,
    Guid ClassKey,
    string Status,
    DateTime? HoldExpiresUtc,
    DateTime ClassStartUtc,
    DateTime? ReminderSentUtc);
