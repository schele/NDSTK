namespace NDSTK.Booking.Domain;

/// <summary>
/// The one fact about a child that affects what a booking costs.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="MemberState"/> because the welcome price is per child while the
/// membership is per account. Folding them together is exactly how a second child on a family
/// account would silently inherit their sibling's spent discount.
/// </remarks>
/// <param name="FirstClassUsed">
/// True once a payment that charged <em>this child</em> the welcome price has completed.
/// </param>
public sealed record ParticipantState(bool FirstClassUsed);
