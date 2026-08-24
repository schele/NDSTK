namespace NDSTK.Booking.Domain;

/// <summary>
/// The two facts about a member that affect what a booking costs. Both are stored as member type
/// properties so an administrator can comp a membership from the backoffice.
/// </summary>
/// <param name="MembershipPaidUntil">Inclusive last day of the paid membership; null when never paid.</param>
/// <param name="FirstClassDiscountUsed">Set once a payment including the welcome price completes.</param>
public sealed record MemberState(DateOnly? MembershipPaidUntil, bool FirstClassDiscountUsed);
