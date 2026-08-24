namespace NDSTK.Booking.Domain;

/// <summary>
/// The two facts about an account that affect what a booking costs. Both are stored as member type
/// properties so an administrator can comp a membership, or grant a family account, from the
/// backoffice without touching SQL.
/// </summary>
/// <param name="MembershipPaidUntil">Inclusive last day of the paid membership; null when never paid.</param>
/// <param name="IsFamilyAccount">
/// True when the account may hold more than one participant. It costs a supplement charged
/// alongside the annual fee, so a renewal is one fee or two depending on this flag alone.
/// </param>
public sealed record MemberState(DateOnly? MembershipPaidUntil, bool IsFamilyAccount);
