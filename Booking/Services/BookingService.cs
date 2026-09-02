using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using Umbraco.Cms.Core.Scoping;

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

    /// <summary>
    /// Paid, but the booking carried a status settlement does not know. Nothing else was done, and
    /// the log says so at Error level: guessing here would mean minting credits on a guess.
    /// </summary>
    Unresolved,
}

/// <summary>What pressing "Betala med Swish" did.</summary>
public enum StartPaymentResult
{
    Started,

    /// <summary>A request already exists: a second tab, or a refresh. The page shows it.</summary>
    AlreadyStarted,

    /// <summary>The payment is no longer pending. The page shows the outcome.</summary>
    NotPending,

    /// <summary>Swish refused or could not be reached. Nothing changed; the member can retry.</summary>
    ProviderUnavailable,
}

/// <summary>What pressing "Avbryt" did.</summary>
public enum CancelPaymentResult
{
    Cancelled,

    /// <summary>Swish had already decided - typically PAID, a second after the press. Applied.</summary>
    AlreadyFinal,

    /// <summary>Swish could not be reached, so nothing was cancelled anywhere. The hold stands.</summary>
    ProviderUnavailable,
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
    ICoreScopeProvider scopeProvider,
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
    ///
    /// Atomic, too. One ambient scope wraps the winning write and every side effect; the
    /// repository's own scopes and the member service's join it, and only this scope's Complete
    /// commits. So an exception after the win - a transient database error while confirming the
    /// booking, say - rolls the payment back to Pending, and the next trigger tries again. Without
    /// that, a paid payment could be left with no place and nothing that would ever repair it.
    /// </remarks>
    public async Task<SettlementResult> SettlePaymentAsync(PaymentRecord payment, string? bankReference = null)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(SwedishTime.ToSwedish(nowUtc));

        using ICoreScope scope = scopeProvider.CreateCoreScope();

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, PaymentStatus.Paid, nowUtc, bankReference, errorCode: null);

        if (won is false)
        {
            logger.LogInformation("Payment {Reference} was already settled; nothing to do.", payment.Reference);
            scope.Complete();
            return SettlementResult.AlreadySettled;
        }

        BookingRecord? booking = payment.BookingId is { } bookingId
            ? await repository.GetBookingAsync(bookingId)
            : null;

        SettlementResult result = booking is null
            ? SettlementResult.NoBooking
            : await PlaceForPaidBookingAsync(payment, booking, nowUtc);

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
        // The stamp is per child, reached through the booking, and conditional on still being null -
        // so two classes booked for the same child at the same moment cannot both claim to have
        // been the first, and a sibling's stamp is never touched. Stamped even when the place
        // became a credit: the welcome price was paid, and the credit is worth a class.
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

        scope.Complete();

        logger.LogInformation("Payment {Reference} settled: {Result}.", payment.Reference, result);
        return result;
    }

    /// <summary>
    /// Gives a paid booking its place. Normally the booking is still Pending. When the hold ran out
    /// first - a slow BankID, a lost callback - the place is taken back if the class has room, and
    /// otherwise the member receives a credit, exactly as a cancellation would give them.
    /// </summary>
    private async Task<SettlementResult> PlaceForPaidBookingAsync(
        PaymentRecord payment, BookingRecord booking, DateTime nowUtc)
    {
        switch (booking.Status)
        {
            case BookingStatus.Pending:
                await repository.ConfirmBookingAsync(booking.Id, nowUtc);
                return SettlementResult.Confirmed;

            case BookingStatus.Confirmed:
                return SettlementResult.Confirmed;

            case BookingStatus.Expired:
                // Expiring the hold already gave back any credit spent on this booking. When a
                // credit paid for the class, the payment carried only the annual fee or the
                // supplement, and the member already holds what they are owed for the place: the
                // returned credit. Taking the place back would hand them class and credit both;
                // minting another would pay them twice. So only a class paid for with money is
                // re-confirmed or compensated here.
                if (payment.ClassFeeOre == 0)
                {
                    logger.LogInformation(
                        "Booking {BookingId} was paid after its hold lapsed; the credit that covered "
                        + "the class was already returned when the hold expired.", booking.Id);
                    return SettlementResult.Credited;
                }

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

                await repository.IssueCreditAsync(payment.MemberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after its hold lapsed and the class had filled; "
                    + "a credit was issued instead.", booking.Id);
                return SettlementResult.Credited;

            case BookingStatus.Cancelled:
                // An editor withdrew the class while the booking was pending. CancelAllForClassAsync
                // credits only confirmed bookings and returns no spent credit, so whether the class
                // was paid with money or with a credit, the member is owed one credit and has none.
                await repository.IssueCreditAsync(payment.MemberKey, booking.Id, nowUtc);
                logger.LogWarning(
                    "Booking {BookingId} was paid after being cancelled; a credit was issued.", booking.Id);
                return SettlementResult.Credited;

            default:
                logger.LogError(
                    "Booking {BookingId} has status {Status}, which settlement does not know. The "
                    + "payment is recorded as paid and nothing else was done.",
                    booking.Id, booking.Status);
                return SettlementResult.Unresolved;
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
    /// <remarks>
    /// One ambient scope, for the same reason as <see cref="SettlePaymentAsync"/>: the payment must
    /// not be recorded as abandoned while the place it held stays reserved.
    /// </remarks>
    public async Task<bool> AbandonPaymentAsync(PaymentRecord payment, string status, string? errorCode = null)
    {
        DateTime nowUtc = DateTime.UtcNow;

        using ICoreScope scope = scopeProvider.CreateCoreScope();

        var won = await repository.TryCompletePaymentAsync(
            payment.Id, status, nowUtc, bankReference: null, errorCode);

        if (won is false)
        {
            logger.LogInformation(
                "Payment {Reference} was already settled; not abandoning it.", payment.Reference);
            scope.Complete();
            return false;
        }

        if (payment.BookingId is { } bookingId)
        {
            await repository.ExpireBookingAsync(bookingId, nowUtc);
        }

        scope.Complete();

        logger.LogInformation(
            "Payment {Reference} abandoned with status {Status} {ErrorCode}.",
            payment.Reference, status, errorCode ?? "-");
        return true;
    }

    /// <summary>
    /// Creates the request at the provider and records it. Restarts the hold, so the reservation
    /// outlives Swish's own timeout however long the member looked at the page first.
    /// </summary>
    public async Task<StartPaymentResult> StartPaymentAsync(PaymentRecord payment, string callbackUrl)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            return StartPaymentResult.NotPending;
        }

        if (payment.ProviderReference is not null)
        {
            return StartPaymentResult.AlreadyStarted;
        }

        DateTime nowUtc = DateTime.UtcNow;
        var context = new PaymentStartContext(callbackUrl, await MessageForAsync(payment));

        PaymentStart start;
        try
        {
            start = await paymentProvider.StartAsync(payment, context);
        }
        catch (PaymentProviderException exception) when (exception.ErrorCode == "RP09")
        {
            // The instruction id is the payment's own Guid, so RP09 means a request under it already
            // exists at Swish: another tab pressed Betala a moment ago and its record is landing now.
            // Nothing to withdraw; the page shows whichever state the row has when it reloads.
            logger.LogWarning(
                "Payment {Reference} already has a request at the provider; a second start was ignored.",
                payment.Reference);
            return StartPaymentResult.AlreadyStarted;
        }
        catch (PaymentProviderException exception)
        {
            logger.LogWarning(
                "Payment {Reference} could not be started at the provider: {ErrorCode}.",
                payment.Reference, exception.ErrorCode ?? "unreachable");
            return StartPaymentResult.ProviderUnavailable;
        }

        var recorded = await repository.TryStartPaymentAsync(
            payment.Id, start.ProviderReference, start.Token, start.CallbackIdentifier, nowUtc);

        if (recorded is false)
        {
            PaymentRecord? current = await repository.GetPaymentByReferenceAsync(payment.Reference);

            if (current?.Status == PaymentStatus.Pending)
            {
                // Another tab recorded first. Under Swish the request just created IS the one the
                // winner recorded - same instruction id - so there is nothing to withdraw. Under the
                // mock there is nothing at all.
                return StartPaymentResult.AlreadyStarted;
            }

            // The payment left Pending between the check above and the write: swept, cancelled or
            // settled. The request just created has no row that will ever reconcile it, so it is
            // withdrawn before the member can pay it in the app.
            try
            {
                await paymentProvider.CancelAsync(start.ProviderReference);
            }
            catch (PaymentProviderException)
            {
                logger.LogWarning(
                    "Request {InstructionId} for payment {Reference}, which is no longer pending, could "
                    + "not be withdrawn.", start.ProviderReference, payment.Reference);
            }

            return StartPaymentResult.NotPending;
        }

        if (payment.BookingId is { } bookingId)
        {
            await repository.TryRestartHoldAsync(
                bookingId, nowUtc.AddMinutes(settings.Get().PaymentHoldMinutes));
        }

        logger.LogInformation("Payment {Reference} started as {InstructionId}.", payment.Reference, start.ProviderReference);
        return StartPaymentResult.Started;
    }

    /// <summary>
    /// Asks the provider where the payment stands and applies the answer. The one routine behind
    /// the page's poll, Swish's callback and the reminder job. Throws
    /// <see cref="PaymentProviderException"/> when the provider cannot be reached; callers decide
    /// whether that is worth more than a log line.
    /// </summary>
    public async Task<PaymentRecord> ReconcileAsync(PaymentRecord payment, DateTime nowUtc)
    {
        if (payment.Status != PaymentStatus.Pending || payment.ProviderReference is null)
        {
            return payment;
        }

        await repository.StampPaymentCheckedAsync(payment.Id, nowUtc);

        PaymentOutcome outcome = await paymentProvider.RetrieveAsync(payment.ProviderReference);
        await ApplyOutcomeAsync(payment, outcome);

        return await repository.GetPaymentByReferenceAsync(payment.Reference) ?? payment;
    }

    /// <summary>
    /// Withdraws the request at the provider, then abandons the payment and releases the place.
    /// </summary>
    /// <remarks>
    /// If the provider cannot be reached, or does not confirm the cancellation, nothing is
    /// cancelled locally either. Cancelling here while the request stays open at Swish would let
    /// the member pay in the app for a payment this site no longer expects; the hold simply runs
    /// out instead.
    /// </remarks>
    public async Task<CancelPaymentResult> CancelPaymentAsync(PaymentRecord payment)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            return CancelPaymentResult.AlreadyFinal;
        }

        if (payment.ProviderReference is not null)
        {
            PaymentOutcome outcome;
            try
            {
                outcome = await paymentProvider.CancelAsync(payment.ProviderReference);
            }
            catch (PaymentProviderException)
            {
                logger.LogWarning(
                    "Payment {Reference} could not be cancelled at the provider; leaving it pending.",
                    payment.Reference);
                return CancelPaymentResult.ProviderUnavailable;
            }

            if (outcome.IsTerminal is false)
            {
                // Swish did not cancel it and it is still open: a refusal that was not "already
                // final", or a status this site does not know. Nothing changed anywhere, and saying
                // otherwise would tell the member a request they can still pay is over.
                logger.LogWarning(
                    "Payment {Reference} could not be cancelled at the provider and is still open.",
                    payment.Reference);
                return CancelPaymentResult.ProviderUnavailable;
            }

            if (outcome.Status != ProviderStatus.Cancelled)
            {
                // Swish had already decided. Whatever it decided is what happened.
                await ApplyOutcomeAsync(payment, outcome);
                return CancelPaymentResult.AlreadyFinal;
            }
        }

        // False means a callback or poll settled it a moment ago; the row, not this press, is the truth.
        return await AbandonPaymentAsync(payment, PaymentStatus.Cancelled)
            ? CancelPaymentResult.Cancelled
            : CancelPaymentResult.AlreadyFinal;
    }

    private async Task ApplyOutcomeAsync(PaymentRecord payment, PaymentOutcome outcome)
    {
        switch (outcome.Status)
        {
            case ProviderStatus.Paid:
                await SettlePaymentAsync(payment, outcome.BankReference);
                break;

            case ProviderStatus.Declined:
            case ProviderStatus.Cancelled:
                await AbandonPaymentAsync(payment, PaymentStatus.Cancelled);
                break;

            case ProviderStatus.Error:
                await AbandonPaymentAsync(payment, PaymentStatus.Failed, outcome.ErrorCode);
                break;

            case ProviderStatus.Created:
                break;
        }
    }

    /// <summary>The text in the member's Swish history, built from the class so it always validates.</summary>
    private async Task<string> MessageForAsync(PaymentRecord payment)
    {
        if (payment.BookingId is null)
        {
            return SwishRequest.Message(null, null);
        }

        BookingRecord? booking = await repository.GetBookingAsync(payment.BookingId.Value);
        TrainingClass? trainingClass = booking is null ? null : classes.Find(booking.ClassKey);

        return SwishRequest.Message(
            trainingClass?.Title ?? "Träning",
            booking is null ? null : SwedishTime.ToSwedish(booking.ClassStartUtc));
    }
}
