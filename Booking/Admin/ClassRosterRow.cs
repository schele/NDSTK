namespace NDSTK.Booking.Admin;

/// <summary>One line of a class roster: the child, how to reach their guardian, and what they paid.</summary>
/// <param name="PaidOre">
/// What the class cost on this booking, or null when nothing did - a place covered by a credit has
/// no class fee. Deliberately not the payment total: on one class's roster a column headed
/// "Betalning" reads as the price of that class, and the first booking of a membership year comes to
/// 350 for a class that cost 100. The whole payment, split three ways, is on the member's row in the
/// Medlemmar dashboard.
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
