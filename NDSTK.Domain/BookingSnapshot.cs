namespace NDSTK.Booking.Domain;

/// <summary>
/// Just enough of a booking for the pure rules to work with, so capacity, reminders and the
/// one-live-booking check need no database.
/// </summary>
/// <param name="MemberKey">
/// The account that pays. Carried alongside <paramref name="ParticipantKey"/> rather than reached
/// through a join, because every payment, credit and reminder query keys off it.
/// </param>
/// <param name="ParticipantKey">
/// The child who attends. What the capacity and duplicate-booking rules use, so that two siblings
/// on one family account can both take a place on the same class.
/// </param>
/// <param name="ClassStartUtc">
/// The booking's own copy of the class start time. Carried here so a booking still renders and
/// still reminds correctly even if the class node is later deleted.
/// </param>
public sealed record BookingSnapshot(
    int Id,
    Guid MemberKey,
    Guid ParticipantKey,
    Guid ClassKey,
    string Status,
    DateTime? HoldExpiresUtc,
    DateTime ClassStartUtc,
    DateTime? ReminderSentUtc);
