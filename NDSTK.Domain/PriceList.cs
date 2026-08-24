namespace NDSTK.Booking.Domain;

/// <summary>
/// The club's prices, in öre. Öre rather than kronor decimals because SQLite maps decimal to
/// REAL, and floating point has no business in a payment record.
/// </summary>
/// <param name="FamilyFeeOre">
/// The supplement that turns an account into a family account, per year. Deliberately second in
/// the list: a positional construction that forgets it then fails to compile, rather than quietly
/// shifting the class price into the membership slot.
/// </param>
public sealed record PriceList(
    int MembershipFeeOre, int FamilyFeeOre, int FirstClassPriceOre, int ClassPriceOre);
