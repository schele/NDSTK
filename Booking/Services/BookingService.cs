using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;

namespace NDSTK.Booking.Services;

/// <summary>Why a booking attempt did not result in a place.</summary>
public enum BookingFailure
{
    None,
    ClassNotFound,
    ClassHasStarted,
    ClassIsFull,
    AlreadyBooked,
    NoCreditAvailable,
}

/// <summary>
/// The outcome of asking to book a class. Exactly one of <see cref="Failure"/> and a booking is
/// meaningful, and <see cref="PaymentReference"/> is set only when the member owes money.
/// </summary>
public sealed record BookingAttempt(
    BookingFailure Failure,
    int? BookingId = null,
    Guid? PaymentReference = null,
    BookingQuote? Quote = null)
{
    public bool Succeeded => Failure == BookingFailure.None;

    /// <summary>True when the member must be sent to the payment page.</summary>
    public bool NeedsPayment => PaymentReference is not null;
}

/// <summary>
/// Turns "I want that class" into a reserved place and, where money is owed, a pending payment.
/// </summary>
public sealed class BookingService(
    IBookingRepository repository,
    TrainingClassService classes,
    MemberProfileService profiles,
    MembershipSettingsService settings,
    IPaymentProvider paymentProvider,
    ILogger<BookingService> logger)
{
    public async Task<BookingAttempt> BookAsync(Guid memberKey, Guid classKey, bool useCredit)
    {
        TrainingClass? trainingClass = classes.Find(classKey);
        if (trainingClass is null)
        {
            return new BookingAttempt(BookingFailure.ClassNotFound);
        }

        DateTime nowUtc = DateTime.UtcNow;
        if (trainingClass.StartUtc <= nowUtc)
        {
            return new BookingAttempt(BookingFailure.ClassHasStarted);
        }

        MembershipSettings config = settings.Get();

        // Checked before reserving so the member gets "du är redan bokad" rather than tripping the
        // partial unique index and seeing an error page. The index remains the real guarantee.
        IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>> existing =
            await repository.GetBookingsByClassAsync([classKey]);

        IReadOnlyList<BookingSnapshot> forClass =
            existing.TryGetValue(classKey, out IReadOnlyList<BookingSnapshot>? found) ? found : [];

        if (Capacity.HasLiveBooking(forClass, memberKey, nowUtc))
        {
            return new BookingAttempt(BookingFailure.AlreadyBooked);
        }

        // The credit is chosen before the place is reserved, but only spent after, so a member who
        // asked to use one and then found the class full has not lost it.
        CreditSnapshot? credit = null;
        if (useCredit)
        {
            credit = Credits.NextSpendable(await repository.GetCreditsForMemberAsync(memberKey));
            if (credit is null)
            {
                return new BookingAttempt(BookingFailure.NoCreditAvailable);
            }
        }

        MemberState member = await profiles.GetStateAsync(memberKey);
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));
        BookingQuote quote = Pricing.Quote(member, config.Prices, credit is not null, today);

        DateTime holdExpires = nowUtc.AddMinutes(config.PaymentHoldMinutes);

        int? bookingId = await repository.TryReservePlaceAsync(
            memberKey, classKey, trainingClass.StartUtc, trainingClass.Capacity, nowUtc, holdExpires);

        if (bookingId is null)
        {
            return new BookingAttempt(BookingFailure.ClassIsFull);
        }

        if (credit is not null && await repository.TrySpendCreditAsync(credit.Id, bookingId.Value, nowUtc) is false)
        {
            // Something else spent the credit between choosing it and reserving the place. Release
            // the place rather than quietly charging the member instead: they asked to use a credit.
            logger.LogInformation(
                "Credit {CreditId} was spent elsewhere; releasing booking {BookingId}.",
                credit.Id, bookingId);

            await repository.ExpireBookingAsync(bookingId.Value, nowUtc);
            return new BookingAttempt(BookingFailure.NoCreditAvailable);
        }

        if (quote.RequiresPayment is false)
        {
            // A paid-up member spending a credit owes nothing, so there is no Swish step at all.
            await repository.ConfirmBookingAsync(bookingId.Value, nowUtc);
            logger.LogInformation("Booking {BookingId} confirmed with no payment due.", bookingId);
            return new BookingAttempt(BookingFailure.None, bookingId, null, quote);
        }

        var payment = new PaymentRecord
        {
            Reference = Guid.NewGuid(),
            MemberKey = memberKey,
            BookingId = bookingId,
            AmountOre = quote.TotalOre,
            MembershipFeeOre = quote.MembershipDueOre,
            ClassFeeOre = quote.ClassFeeOre,
            Status = PaymentStatus.Pending,
            Provider = paymentProvider.Name,
            CreatedUtc = nowUtc,
        };

        var paymentId = await repository.CreatePaymentAsync(payment);
        await repository.LinkPaymentAsync(bookingId.Value, paymentId);

        logger.LogInformation(
            "Booking {BookingId} is holding a place pending payment of {AmountOre} öre.",
            bookingId, quote.TotalOre);

        return new BookingAttempt(BookingFailure.None, bookingId, payment.Reference, quote);
    }

    /// <summary>
    /// Completes a payment: confirms the booking, extends the membership if the fee was included,
    /// and marks the welcome price used if it was charged.
    /// </summary>
    public async Task SettlePaymentAsync(PaymentRecord payment)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));

        await repository.CompletePaymentAsync(payment.Id, PaymentStatus.Paid, nowUtc);

        if (payment.BookingId is { } bookingId)
        {
            await repository.ConfirmBookingAsync(bookingId, nowUtc);
        }

        if (payment.MembershipFeeOre > 0)
        {
            await profiles.ExtendMembershipAsync(payment.MemberKey, today);
        }

        // Deliberately not "did this payment equal the welcome price". Comparing the stored amount
        // against the configured price would break the moment an editor changes prices between a
        // booking and its payment, and would misfire entirely if the two prices were ever set the
        // same. Instead: Pricing only ever quotes the welcome price while the member's flag is
        // still false, so a class fee charged to a member whose flag is false *was* the welcome
        // price, whatever the numbers now say.
        //
        // Two classes booked at the same moment could both be quoted the welcome price and both
        // settle. The member was quoted honestly each time, so the club honours it; locking to
        // prevent that is not worth the contention.
        MemberState after = await profiles.GetStateAsync(payment.MemberKey);
        if (payment.ClassFeeOre > 0 && after.FirstClassDiscountUsed is false)
        {
            await profiles.MarkFirstClassDiscountUsedAsync(payment.MemberKey);
        }

        logger.LogInformation("Payment {Reference} settled.", payment.Reference);
    }

    /// <summary>
    /// Cancels a booking and issues a credit. Returns false when the booking was not the member's,
    /// or is not a confirmed future booking - in which case nothing changed.
    /// </summary>
    /// <remarks>
    /// No money is returned, by design: the club keeps the fee and the member keeps a place to use
    /// on another class. Every precondition is enforced in the repository's UPDATE rather than
    /// checked here first, so two simultaneous submissions cannot both succeed and mint two credits.
    /// </remarks>
    public async Task<bool> CancelAsync(Guid memberKey, int bookingId)
    {
        var cancelled = await repository.TryCancelBookingAsync(bookingId, memberKey, DateTime.UtcNow);

        if (cancelled)
        {
            logger.LogInformation("Booking {BookingId} cancelled; a credit was issued.", bookingId);
        }
        else
        {
            logger.LogInformation(
                "Booking {BookingId} was not cancelled: not the member's, not confirmed, or already started.",
                bookingId);
        }

        return cancelled;
    }

    /// <summary>Abandons a payment and releases the place it was holding.</summary>
    public async Task AbandonPaymentAsync(PaymentRecord payment, string status)
    {
        DateTime nowUtc = DateTime.UtcNow;

        await repository.CompletePaymentAsync(payment.Id, status, nowUtc);

        if (payment.BookingId is { } bookingId)
        {
            await repository.ExpireBookingAsync(bookingId, nowUtc);
        }

        logger.LogInformation("Payment {Reference} abandoned with status {Status}.", payment.Reference, status);
    }
}
