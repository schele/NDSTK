namespace NDSTK.Booking.Admin;

/// <summary>One line of a class roster: the child, and how to reach their guardian.</summary>
public sealed record ClassRosterRow(
    int BookingId,
    string ChildName,
    int? Age,
    string GuardianName,
    string GuardianEmail,
    string? GuardianPhone,
    string Status,
    DateTime CreatedUtc);
