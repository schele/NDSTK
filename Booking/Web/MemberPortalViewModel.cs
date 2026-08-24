using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Web;

/// <summary>Everything the portal page renders, assembled once by the controller.</summary>
public sealed record MemberPortalViewModel(
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

    /// <summary>What the next class will cost, so the portal can say so before the member clicks.</summary>
    public int NextClassFeeOre => FirstClassDiscountAvailable
        ? Prices.FirstClassPriceOre
        : Prices.ClassPriceOre;
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
public sealed record MembershipStatus(bool IsValid, DateOnly? PaidUntil);
