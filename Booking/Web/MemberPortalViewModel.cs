using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Web;

/// <summary>Everything the portal page renders, assembled once by the controller.</summary>
public sealed record MemberPortalViewModel(
    string? Email,
    IReadOnlyList<BookableClass> UpcomingClasses,
    IReadOnlyList<MemberBookingRow> MyBookings,
    IReadOnlyList<MemberChildRow> Children,
    int UnspentCredits,
    MembershipStatus Membership,
    PriceList Prices,
    int ReminderHoursBefore,
    int CancellationDeadlineHours)
{
    /// <summary>
    /// Bookings starting inside the reminder window, which the banner at the top of the page
    /// highlights. A pure read of MyBookings, so the banner can never disagree with the list.
    /// </summary>
    public IReadOnlyList<MemberBookingRow> ReminderBookings =>
    [
        .. MyBookings.Where(row => row.IsUpcoming && row.HoursUntilStart <= ReminderHoursBefore),
    ];

    /// <summary>
    /// A solo account may have exactly one child; a family account may add more. The rule is also
    /// enforced in the controller - a hidden button is not a rule.
    /// </summary>
    public bool CanAddChild => Membership.IsFamilyAccount || Children.Count == 0;

    /// <summary>The class fee alone for this child's next booking.</summary>
    public int NextClassFeeOreFor(MemberChildRow child) => child.FirstClassAvailable
        ? Prices.FirstClassPriceOre
        : Prices.ClassPriceOre;

    /// <summary>
    /// What the member will actually be charged for this child's next booking, membership and
    /// family fees included when they are due. This is what the booking button shows: quoting the
    /// class fee alone and then presenting a larger figure on the payment page reads as a bait and
    /// switch.
    /// </summary>
    /// <remarks>
    /// This mirrors <see cref="Pricing.Quote"/>. If the two ever disagree the member is quoted one
    /// price and charged another, so change them together.
    /// </remarks>
    public int NextBookingTotalOreFor(MemberChildRow child)
    {
        var membershipDue = Membership.IsValid ? 0 : Prices.MembershipFeeOre;
        var familyDue = Membership.IsValid || Membership.IsFamilyAccount is false
            ? 0
            : Prices.FamilyFeeOre;

        return NextClassFeeOreFor(child) + membershipDue + familyDue;
    }

    /// <summary>The child a single-child account books for without being asked.</summary>
    public MemberChildRow? OnlyChild => Children.Count == 1 ? Children[0] : null;

    /// <summary>
    /// The classes worth showing under "Boka träning": the ones there is still someone to book.
    /// </summary>
    /// <remarks>
    /// A class every child is already on drops out. It is in "Mina bokningar" a few lines above,
    /// and leaving it here - stripped of its buttons, carrying only a "Bokad:" label - reads as
    /// something you failed to do rather than something you have already done.
    ///
    /// A family with one child still to book keeps the class: there is a real action left on it.
    /// </remarks>
    public IReadOnlyList<BookableClass> ClassesToOffer =>
    [
        .. UpcomingClasses.Where(bookable => bookable.EveryChildBooked is false),
    ];
}

/// <summary>One row in "Mina barn".</summary>
public sealed record MemberChildRow(
    Guid Key,
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    bool FirstClassAvailable)
{
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// False only for a child the backfill created, who has no real birth date yet. Booking is
    /// refused until it is filled in - see <see cref="Services.BookingFailure.ParticipantIncomplete"/>.
    /// </summary>
    public bool IsComplete => BirthDate is not null;

    /// <summary>ÅÅÅÅMMDD, the form a Swedish parent types without being asked.</summary>
    public string BirthDateCompact =>
        BirthDate is { } date ? SwedishDate.ToCompact(date) : string.Empty;

    public int? Age => BirthDate is { } date
        ? SwedishDate.AgeOn(date, DateOnly.FromDateTime(DateTime.UtcNow))
        : null;
}

/// <summary>One row in "Mina bokningar".</summary>
public sealed record MemberBookingRow(
    int BookingId,
    TrainingClass? Class,
    string ChildName,
    string Status,
    DateTime ClassStartUtc,
    bool UsedCredit)
{
    public bool IsUpcoming => ClassStartUtc > DateTime.UtcNow;

    public double HoursUntilStart => (ClassStartUtc - DateTime.UtcNow).TotalHours;

    // Whether this booking can still be cancelled is deliberately NOT here. It depends on the
    // club's cancellation deadline, which is one setting for the whole club - see
    // MemberBookingsPanel.CanCancel. A row that answered it from its own state would have to carry
    // a copy of that setting, and two rows could then disagree.
}

/// <summary>Whether the annual fee is paid, until when, and whether this is a family account.</summary>
/// <param name="SupplementPaidThisYear">
/// True when the family supplement has already been paid for the current membership year - which
/// happens when an account was downgraded to solo part-way through it. Re-activating is then free,
/// and the button has to say so rather than quoting a price nobody will be charged.
/// </param>
public sealed record MembershipStatus(
    bool IsValid,
    DateOnly? PaidUntil,
    bool IsFamilyAccount,
    bool SupplementPaidThisYear = false)
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
