using NDSTK.Booking.Domain;

namespace NDSTK.Booking.Data;

/// <summary>
/// All SQL for the booking feature. An interface so the services above it can be reasoned about -
/// and later tested - without a database.
/// </summary>
/// <remarks>
/// The read methods deliberately return the pure <see cref="BookingSnapshot"/> and
/// <see cref="CreditSnapshot"/> records rather than the NPoco POCOs. That keeps the rules in
/// NDSTK.Domain free of any persistence type, and it means a query change cannot quietly alter what
/// the rules see.
/// </remarks>
public interface IBookingRepository
{
    /// <summary>
    /// Every booking for the given classes, whatever its status - the capacity rule needs the
    /// cancelled and expired ones too, in order to discount them.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>>> GetBookingsByClassAsync(
        IReadOnlyCollection<Guid> classKeys);

    /// <summary>One member's bookings, newest class first.</summary>
    Task<IReadOnlyList<BookingSnapshot>> GetBookingsForMemberAsync(Guid memberKey);

    /// <summary>One member's credits, spent and unspent.</summary>
    Task<IReadOnlyList<CreditSnapshot>> GetCreditsForMemberAsync(Guid memberKey);

    /// <summary>
    /// Whether a completed payment has carried the family supplement since the given moment.
    /// </summary>
    /// <remarks>
    /// Asked with the start of the current membership year, to answer "have they already paid the
    /// supplement for this year?". A member who dropped back to one child and then changed their
    /// mind must not be billed for the same year twice - the account was downgraded on their
    /// behalf, so re-activating it is putting back something they had already bought.
    /// </remarks>
    Task<bool> HasPaidFamilyFeeSinceAsync(Guid memberKey, DateTime sinceUtc);

    // ------------------------------------------------------------------- writes

    /// <summary>
    /// Reserves a place, or returns null when the class is already full.
    /// </summary>
    /// <remarks>
    /// The count and the insert are one statement, so two members clicking at the same moment
    /// cannot both pass a check that says "one place left". Doing it in two statements would leave
    /// exactly that gap, however short.
    /// </remarks>
    Task<int?> TryReservePlaceAsync(
        Guid memberKey, Guid participantKey, Guid classKey, DateTime classStartUtc, int capacity,
        DateTime nowUtc, DateTime holdExpiresUtc);

    /// <summary>
    /// Spends a credit on a booking, or returns false if something else spent it first. The
    /// condition is in the UPDATE, so a credit cannot be spent twice.
    /// </summary>
    Task<bool> TrySpendCreditAsync(int creditId, int bookingId, DateTime nowUtc);

    Task<int> CreatePaymentAsync(PaymentRecord payment);

    /// <summary>Attaches a payment to its booking, once the payment row has an id.</summary>
    Task LinkPaymentAsync(int bookingId, int paymentId);

    Task<PaymentRecord?> GetPaymentByReferenceAsync(Guid reference);

    Task<BookingRecord?> GetBookingAsync(int bookingId);

    /// <summary>Marks the booking confirmed and clears its payment hold.</summary>
    Task ConfirmBookingAsync(int bookingId, DateTime nowUtc);

    Task CompletePaymentAsync(int paymentId, string status, DateTime nowUtc);

    /// <summary>
    /// Releases an abandoned or failed booking, and returns any credit spent on it so the member is
    /// not left out of pocket for a payment that never completed.
    /// </summary>
    Task ExpireBookingAsync(int bookingId, DateTime nowUtc);

    // -------------------------------------------------------- background job

    /// <summary>
    /// Confirmed bookings starting inside the window that have not been reminded yet, together with
    /// the member each belongs to. One query rather than a scan of every booking ever made.
    /// </summary>
    Task<IReadOnlyList<BookingRecord>> GetBookingsDueRemindersAsync(DateTime nowUtc, DateTime windowEndUtc);

    /// <summary>Pending bookings whose payment hold has run out, so their places can be released.</summary>
    Task<IReadOnlyList<BookingRecord>> GetExpiredHoldsAsync(DateTime nowUtc);

    /// <summary>
    /// Stamps a booking as reminded. Conditional on the stamp still being null, so two overlapping
    /// job runs cannot both send the same reminder.
    /// </summary>
    Task<bool> TryStampReminderSentAsync(int bookingId, DateTime nowUtc);

    // ----------------------------------------------------- editor changes

    /// <summary>
    /// Repoints every live booking for a class at a new start time, and clears the reminder stamp on
    /// any that had already been reminded, so the member is told about the change.
    /// </summary>
    /// <returns>How many bookings were affected.</returns>
    Task<int> ResyncClassStartAsync(Guid classKey, DateTime newStartUtc, DateTime nowUtc);

    /// <summary>
    /// Cancels every live booking for a class and issues a credit for each confirmed one, for when
    /// an editor withdraws a class people had already paid for.
    /// </summary>
    /// <returns>How many credits were issued.</returns>
    Task<int> CancelAllForClassAsync(Guid classKey, DateTime nowUtc);

    /// <summary>
    /// Cancels a confirmed booking and issues exactly one credit for it. Returns false when the
    /// booking was not the caller's, or was not confirmed - so a double submission cannot mint a
    /// second credit.
    /// </summary>
    Task<bool> TryCancelBookingAsync(int bookingId, Guid memberKey, DateTime nowUtc);
}
