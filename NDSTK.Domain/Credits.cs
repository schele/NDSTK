namespace NDSTK.Booking.Domain;

/// <summary>
/// Chooses which credit to spend. Deciding is pure; the actual spend is a conditional UPDATE in
/// the repository, so two concurrent bookings cannot spend the same credit even though both were
/// offered it here.
/// </summary>
public static class Credits
{
    public static int CountUnspent(IEnumerable<CreditSnapshot> credits)
        => credits.Count(credit => credit.SpentOnBookingId is null);

    /// <summary>Oldest first, so credits are used in the order they were earned.</summary>
    public static CreditSnapshot? NextSpendable(IEnumerable<CreditSnapshot> credits)
        => credits
            .Where(credit => credit.SpentOnBookingId is null)
            .OrderBy(credit => credit.Id)
            .FirstOrDefault();
}
