namespace NDSTK.Booking.Admin;

/// <summary>One account, as the Medlemmar dashboard lists it.</summary>
/// <remarks>
/// Money stays in öre all the way to the browser, where the dashboard divides by a hundred once.
/// That is the same rule the server side follows - the conversion happens in exactly one place.
/// </remarks>
public sealed record MemberAdminRow(
    Guid MemberKey,
    string Name,
    string Email,
    string? Phone,
    bool IsFamilyAccount,
    DateTime? VerifiedUtc,
    DateTime? MemberSinceUtc,
    DateOnly? PaidUntil,
    int TotalPaidOre,
    DateTime? LastPaymentUtc,
    int ParticipantCount,
    int ConfirmedBookings,
    int CancelledBookings,
    int ExpiredBookings,
    int UnspentCredits,
    IReadOnlyList<string> ChildNames)
{
    /// <summary>
    /// Negative once the membership has lapsed, which the dashboard renders as "Utgången" rather
    /// than as a negative number of days. Null when the account has never paid.
    /// </summary>
    public int? DaysLeft => PaidUntil is { } until
        ? until.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber
        : null;
}
