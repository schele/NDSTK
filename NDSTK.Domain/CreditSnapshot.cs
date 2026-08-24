namespace NDSTK.Booking.Domain;

/// <summary>
/// One booking credit. The link to the booking that spent it lives here and nowhere else, so the
/// two directions cannot drift apart.
/// </summary>
public sealed record CreditSnapshot(int Id, Guid MemberKey, int? SpentOnBookingId);
