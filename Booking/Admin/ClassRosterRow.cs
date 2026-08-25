namespace NDSTK.Booking.Admin;

/// <summary>One line of a class roster: the child, how to reach their guardian, and what they paid.</summary>
/// <param name="PaidOre">
/// What this booking was actually paid, or null when nothing was. The whole payment rather than the
/// class fee alone - the first booking of a membership year legitimately carries the annual fee and
/// the family supplement with it, and the club wants to see the figure that arrived.
/// </param>
/// <param name="UsedCredit">
/// A credit was spent on this place. Not exclusive with <paramref name="PaidOre"/>: a lapsed member
/// spending one pays the annual fee and nothing for the class, so both are true.
/// </param>
public sealed record ClassRosterRow(
    int BookingId,
    string ChildName,
    int? Age,
    string GuardianName,
    string GuardianEmail,
    string? GuardianPhone,
    string Status,
    int? PaidOre,
    bool UsedCredit,
    DateTime CreatedUtc);
