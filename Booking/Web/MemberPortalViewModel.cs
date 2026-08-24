using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Web;

/// <summary>Everything the portal page renders, assembled once by the controller.</summary>
public sealed record MemberPortalViewModel(
    string? Email,
    IReadOnlyList<BookableClass> UpcomingClasses,
    IReadOnlyList<MemberBookingRow> MyBookings,
    int UnspentCredits,
    MembershipStatus Membership,
    PriceList Prices,
    bool FirstClassDiscountAvailable,
    int ReminderHoursBefore)
{
    /// <summary>
    /// Bookings starting inside the reminder window, which the banner at the top of the page
    /// highlights. A pure read of MyBookings, so the banner can never disagree with the list.
    /// </summary>
    public IReadOnlyList<MemberBookingRow> ReminderBookings =>
    [
        .. MyBookings.Where(row => row.IsUpcoming && row.HoursUntilStart <= ReminderHoursBefore),
    ];

    /// <summary>The class fee alone for the member's next booking.</summary>
    public int NextClassFeeOre => FirstClassDiscountAvailable
        ? Prices.FirstClassPriceOre
        : Prices.ClassPriceOre;

    /// <summary>
    /// What the member will actually be charged for their next booking, membership fee included
    /// when it is due. This is what the booking button shows: quoting the class fee alone and then
    /// presenting a larger figure on the payment page would read as a bait and switch.
    /// </summary>
    public int NextBookingTotalOre =>
        NextClassFeeOre + (Membership.IsValid ? 0 : Prices.MembershipFeeOre);
}

/// <summary>One row in "Mina bokningar".</summary>
public sealed record MemberBookingRow(
    int BookingId,
    TrainingClass? Class,
    string Status,
    DateTime ClassStartUtc,
    bool UsedCredit)
{
    public bool IsUpcoming => ClassStartUtc > DateTime.UtcNow;

    public double HoursUntilStart => (ClassStartUtc - DateTime.UtcNow).TotalHours;

    public bool IsCancellable => Status == BookingStatus.Confirmed && IsUpcoming;
}

/// <summary>Whether the annual fee is paid, and until when.</summary>
public sealed record MembershipStatus(bool IsValid, DateOnly? PaidUntil)
{
    /// <summary>
    /// Paid once, but the year has run out. Worth distinguishing from a member who has never paid:
    /// telling someone their membership "will be added" when it actually *lapsed* on a date they
    /// can check reads as a mistake, and the club looks careless.
    /// </summary>
    public bool HasLapsed => IsValid is false && PaidUntil is not null;

    /// <summary>Never paid the annual fee at all.</summary>
    public bool IsNew => IsValid is false && PaidUntil is null;
}
