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

    /// <summary>The participant key did not name a live child of this account.</summary>
    ParticipantNotFound,

    /// <summary>
    /// A child the backfill created, who has no real birth date yet. Only ever reachable for a
    /// member who registered before participants existed.
    /// </summary>
    ParticipantIncomplete,
}

/// <summary>Why a cancellation did or did not happen.</summary>
public enum CancelOutcome
{
    Cancelled,

    /// <summary>Inside the cancellation deadline. Worth its own message: the member did nothing
    /// wrong and the reason is a rule they can plan around next time.</summary>
    TooLate,

    /// <summary>
    /// Not the member's booking, or not confirmed. Deliberately one outcome for several causes:
    /// distinguishing them would tell somebody whether a booking id they guessed exists.
    /// </summary>
    NotCancellable,
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

/// <summary>What settling a paid payment did about the place it was for.</summary>
public enum SettlementResult
{
    /// <summary>Somebody else settled it first. Nothing was changed.</summary>
    AlreadySettled,

    /// <summary>The pending booking is now confirmed.</summary>
    Confirmed,

    /// <summary>The hold had lapsed, but the class still had room, so the place is theirs.</summary>
    Reconfirmed,

    /// <summary>The hold had lapsed and the class filled. The member has a credit instead.</summary>
    Credited,

    /// <summary>A purchase with no booking attached: the family upgrade.</summary>
    NoBooking,
}

/// <summary>
/// Turns "I want that class" into a reserved place and, where money is owed, a pending payment.
/// </summary>
public sealed class BookingService(
    IBookingRepository repository,
    IParticipantRepository participants,
    TrainingClassService classes,
    MemberProfileService profiles,
    MembershipSettingsService settings,
    IPaymentProvider paymentProvider,
    ILogger<BookingService> logger)
{
    public async Task<BookingAttempt> BookAsync(
        Guid memberKey, Guid participantKey, Guid classKey, bool useCredit)
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

        ParticipantRecord? participant = await participants.GetAsync(participantKey);

        // Ownership is verified here rather than trusted from the form: the key arrives on a POST,
        // and a forged one must not book a stranger's child onto a class.
        if (participant is null
            || participant.MemberKey != memberKey
            || participant.RemovedUtc is not null)
        {
            return new BookingAttempt(BookingFailure.ParticipantNotFound);
        }

        // Only ever true for a child the backfill created, who has no real birth date yet. Asking
        // for it once is better than carrying a guessed one through the club's records.
        if (participant.BirthDate is null)
        {
            return new BookingAttempt(BookingFailure.ParticipantIncomplete);
        }

        MembershipSettings config = settings.Get();

        // Checked before reserving so the member gets "du är redan bokad" rather than tripping the
        // partial unique index and seeing an error page. The index remains the real guarantee.
        IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>> existing =
            await repository.GetBookingsByClassAsync([classKey]);

        IReadOnlyList<BookingSnapshot> forClass =
            existing.TryGetValue(classKey, out IReadOnlyList<BookingSnapshot>? found) ? found : [];

        if (Capacity.HasLiveBooking(forClass, participantKey, nowUtc))
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

        // The welcome price is this child's, not the account's: a sibling who has already used
        // theirs must not make this one pay full price, and vice versa.
        var participantState = new ParticipantState(participant.FirstClassUsedUtc is not null);

        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));
        BookingQuote quote = Pricing.Quote(
            member, participantState, config.Prices, credit is not null, today);

        DateTime holdExpires = nowUtc.AddMinutes(config.PaymentHoldMinutes);

        int? bookingId = await repository.TryReservePlaceAsync(
            memberKey, participantKey, classKey, trainingClass.StartUtc, trainingClass.Capacity,
            nowUtc, holdExpires);

        if (bookingId is null)
        {
            // Two reasons the reservation can fail: the class filled up, or this member already has
            // a live booking on it (a double submission that the unique index caught). Re-reading
            // tells them apart, so the member is not told "fullbokad" about a class they are
            // already booked on.
            IReadOnlyDictionary<Guid, IReadOnlyList<BookingSnapshot>> afterwards =
                await repository.GetBookingsByClassAsync([classKey]);

            IReadOnlyList<BookingSnapshot> current =
                afterwards.TryGetValue(classKey, out IReadOnlyList<BookingSnapshot>? rows) ? rows : [];

            return Capacity.HasLiveBooking(current, participantKey, nowUtc)
                ? new BookingAttempt(BookingFailure.AlreadyBooked)
                : new BookingAttempt(BookingFailure.ClassIsFull);
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
            FamilyFeeOre = quote.FamilyDueOre,
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
    /// <remarks>
    /// Idempotent. The first statement moves the payment out of Pending conditionally, and every
    /// side effect below runs only when that statement changed a row. Swish's callback, the
    /// page's poll and the reminder job can all arrive with the same PAID; one of them wins.
    /// </remarks>
    public async Task<SettlementResult> SettlePaymentAsync(PaymentRecord payment, string? bankReference = null)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, PaymentStatus.Paid, nowUtc, bankReference, errorCode: null);

        if (won is false)
        {
            logger.LogInformation("Payment {Reference} was already settled; nothing to do.", payment.Reference);
            return SettlementResult.AlreadySettled;
        }

        BookingRecord? booking = payment.BookingId is { } bookingId
            ? await repository.GetBookingAsync(bookingId)
            : null;

        SettlementResult result = booking is null
            ? SettlementResult.NoBooking
            : await PlaceForPaidBookingAsync(booking, payment.MemberKey, nowUtc);

        if (payment.MembershipFeeOre > 0)
        {
            await profiles.ExtendMembershipAsync(payment.MemberKey, today);
        }

        // Deliberately not "did this payment equal the welcome price". Comparing the stored amount
        // against the configured price would break the moment an editor changes prices between a
        // booking and its payment, and would misfire entirely if the two prices were ever set the
        // same. Instead: Pricing only ever quotes the welcome price while the child's stamp is
        // still null, so a class fee charged to a child whose stamp is null *was* the welcome
        // price, whatever the numbers now say.
        //
        // Stamped even when the place became a credit: the welcome price was paid, and the credit
        // is worth a class.
        if (payment.ClassFeeOre > 0 && booking?.ParticipantKey is { } participantKey)
        {
            await participants.TryStampFirstClassUsedAsync(participantKey, nowUtc);
        }

        // The supplement is charged either alongside the annual fee on a renewal, or on its own as
        // a mid-year upgrade. Either way, paying it makes the account a family account. Note that
        // ExtendMembershipAsync above is guarded on MembershipFeeOre, which an upgrade payment sets
        // to zero - that is what stops the upgrade moving the expiry date.
        if (payment.FamilyFeeOre > 0)
        {
            await profiles.SetFamilyAccountAsync(payment.MemberKey);
        }

        logger.LogInformation("Payment {Reference} settled: {Result}.", payment.Reference, result);
        return result;
    }

    /// <summary>
    /// Gives a paid booking its place. Normally the booking is still Pending. When the hold ran out
    /// first - a slow BankID, a lost callback - the place is taken back if the class has room, and
    /// otherwise the member receives a credit, exactly as a cancellation would give them.
    /// </summary>
    private async Task<SettlementResult> PlaceForPaidBookingAsync(
        BookingRecord booking, Guid memberKey, DateTime nowUtc)
    {
        switch (booking.Status)
        {
            case BookingStatus.Pending:
                await repository.ConfirmBookingAsync(booking.Id, nowUtc);
                return SettlementResult.Confirmed;

            case BookingStatus.Confirmed:
                return SettlementResult.Confirmed;

            case BookingStatus.Expired:
                TrainingClass? trainingClass = classes.Find(booking.ClassKey);

                if (trainingClass is not null
                    && trainingClass.StartUtc > nowUtc
                    && await repository.TryReconfirmBookingAsync(booking.Id, trainingClass.Capacity, nowUtc))
                {
                    logger.LogInformation(
                        "Booking {BookingId} was paid after its hold lapsed; the place was still free.",
                        booking.Id);
                    return SettlementResult.Reconfirmed;
                }

                await repository.IssueCreditAsync(memberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after its hold lapsed and the class had filled; "
                    + "a credit was issued instead.", booking.Id);
                return SettlementResult.Credited;

            default:
                // Cancelled while pending: an editor withdrew the class. CancelAllForClassAsync
                // credits only confirmed bookings, so this one got nothing - until now, when it
                // turns out to have been paid for.
                await repository.IssueCreditAsync(memberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after being cancelled; a credit was issued.", booking.Id);
                return SettlementResult.Credited;
        }
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
    public async Task<CancelOutcome> CancelAsync(Guid memberKey, int bookingId)
    {
        DateTime nowUtc = DateTime.UtcNow;
        var deadlineHours = settings.Get().CancellationDeadlineHours;
        DateTime earliest = Cancellation.EarliestCancellableStart(nowUtc, deadlineHours);

        var cancelled = await repository.TryCancelBookingAsync(
            bookingId, memberKey, nowUtc, earliest);

        if (cancelled)
        {
            logger.LogInformation("Booking {BookingId} cancelled; a credit was issued.", bookingId);
            return CancelOutcome.Cancelled;
        }

        // Work out whether it failed because the window has closed, so the member can be told that
        // rather than the generic refusal. Only reached on failure, and only trusted once the
        // booking is confirmed to be this member's - a booking that is not theirs gets the same
        // answer as one that does not exist.
        BookingRecord? booking = await repository.GetBookingAsync(bookingId);

        if (booking is not null
            && booking.MemberKey == memberKey
            && booking.Status == BookingStatus.Confirmed
            && Cancellation.IsOpen(booking.ClassStartUtc, nowUtc, deadlineHours) is false)
        {
            logger.LogInformation(
                "Booking {BookingId} was not cancelled: inside the {Hours}h cancellation deadline.",
                bookingId, deadlineHours);

            return CancelOutcome.TooLate;
        }

        logger.LogInformation(
            "Booking {BookingId} was not cancelled: not the member's, or not confirmed.", bookingId);

        return CancelOutcome.NotCancellable;
    }

    /// <summary>
    /// Abandons a payment and releases the place it was holding. Returns false when the payment
    /// had already left Pending, in which case nothing changed.
    /// </summary>
    public async Task<bool> AbandonPaymentAsync(PaymentRecord payment, string status, string? errorCode = null)
    {
        DateTime nowUtc = DateTime.UtcNow;

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, status, nowUtc, bankReference: null, errorCode);

        if (won is false)
        {
            logger.LogInformation(
                "Payment {Reference} was already settled; not abandoning it.", payment.Reference);
            return false;
        }

        if (payment.BookingId is { } bookingId)
        {
            await repository.ExpireBookingAsync(bookingId, nowUtc);
        }

        logger.LogInformation(
            "Payment {Reference} abandoned with status {Status}{Code}.",
            payment.Reference, status, errorCode is null ? string.Empty : $" ({errorCode})");
        return true;
    }
}
