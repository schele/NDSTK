namespace NDSTK.Booking.Domain;

/// <summary>
/// The club's prices, in öre. Öre rather than kronor decimals because SQLite maps decimal to
/// REAL, and floating point has no business in a payment record.
/// </summary>
public sealed record PriceList(int MembershipFeeOre, int FirstClassPriceOre, int ClassPriceOre);
