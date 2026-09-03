namespace NDSTK.Booking.Admin;

/// <summary>Everything about one account, for the dashboard's detail panel.</summary>
public sealed record MemberAdminDetail(
    MemberAdminRow Summary,
    IReadOnlyList<AdminPaymentRow> Payments,
    IReadOnlyList<AdminBookingRow> Bookings);

/// <summary>
/// One payment, with its split intact rather than reduced to a total, so the club can answer
/// "how much, and for what" without inferring anything.
/// </summary>
public sealed record AdminPaymentRow(
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    int AmountOre,
    int MembershipFeeOre,
    int FamilyFeeOre,
    int ClassFeeOre,
    string Status,
    string Provider,
    /// <summary>Swish's payment reference once paid: what the bank statement shows. Null for the mock.</summary>
    string? BankReference,
    /// <summary>Swish's error code for a failed payment.</summary>
    string? ErrorCode);

/// <summary>One booking, named by the child it belongs to.</summary>
public sealed record AdminBookingRow(
    string ChildName,
    string ClassName,
    DateTime ClassStartUtc,
    string Status);
