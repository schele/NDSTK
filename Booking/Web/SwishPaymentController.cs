using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Payments;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;

namespace NDSTK.Booking.Web;

/// <summary>What the payment page should render.</summary>
public sealed record SwishPaymentViewModel(
    Guid Reference,
    int AmountOre,
    int MembershipFeeOre,
    int FamilyFeeOre,
    int ClassFeeOre,
    /// <summary>Null for a purchase with no class attached, which today means a family upgrade.</summary>
    int? BookingId,
    string? ClassTitle,
    DateTime? ClassStartUtc,
    string Status,
    string? BookingStatus,
    DateTime? HoldExpiresUtc,
    /// <summary>The mock is active: Demoläge and the simulate buttons.</summary>
    bool IsMock,
    /// <summary>A request exists at the provider: show the app link and QR, and poll.</summary>
    bool IsStarted,
    string? AppLink,
    string? QrUrl,
    string? StatusUrl,
    /// <summary>The sentence for a failed or cancelled payment, from SwishOutcome.</summary>
    string? OutcomeMessage)
{
    public bool IsPending => Status == PaymentStatus.Pending;
    public bool IsPaid => Status == PaymentStatus.Paid;
    public bool IsFailed => Status == PaymentStatus.Failed;
    public bool IsCancelled => Status == PaymentStatus.Cancelled;

    /// <summary>
    /// Paid, but the place was gone by the time the money arrived, so the member holds a credit
    /// instead. True whenever a paid payment's booking is not confirmed - the only way that
    /// happens is the late-payment rule in SettlePaymentAsync.
    /// </summary>
    public bool CreditIssued
        => IsPaid && BookingId is not null && BookingStatus != Domain.BookingStatus.Confirmed;

    /// <summary>
    /// Minutes left on the reservation, rounded up, never below zero.
    /// </summary>
    /// <remarks>
    /// Rounded up rather than truncated. A page rendered a fraction of a second after a hold is
    /// created has a few milliseconds less than the full duration left - which truncates to one
    /// minute fewer and reads as though a minute was lost before the member did anything. Rounding
    /// up shows the whole duration for the first minute, which is what "reserverad i N minuter
    /// till" means. The countdown script uses the same rule, so the two never disagree.
    /// </remarks>
    public int MinutesLeft => HoldExpiresUtc is { } expires
        ? Math.Max(0, (int)Math.Ceiling((expires - DateTime.UtcNow).TotalMinutes))
        : 0;

    /// <summary>
    /// The expiry as an ISO 8601 instant for the countdown script.
    /// </summary>
    /// <remarks>
    /// The kind is forced to UTC before formatting. NPoco hands the value back as
    /// <see cref="DateTimeKind.Unspecified"/>, and "o" omits the trailing Z for that kind - which
    /// JavaScript's Date.parse then reads as *local* time, putting the countdown one or two hours
    /// out depending on the season.
    /// </remarks>
    public string? HoldExpiresIso => HoldExpiresUtc is { } expires
        ? DateTime.SpecifyKind(expires, DateTimeKind.Utc).ToString("o")
        : null;
}

/// <summary>
/// Renders the mocked Swish page.
/// </summary>
/// <remarks>
/// Named after the SwishPayment template rather than Index, for the same routing reason as
/// MemberVerifyController.
/// </remarks>
public sealed class SwishPaymentController(
    ILogger<SwishPaymentController> logger,
    ICompositeViewEngine compositeViewEngine,
    IUmbracoContextAccessor umbracoContextAccessor,
    IMemberManager memberManager,
    IBookingRepository repository,
    Services.TrainingClassService classes,
    MemberBookingsProvider bookingsProvider,
    IPaymentProvider paymentProvider,
    IPublishedContentQuery contentQuery,
    IPublishedUrlProvider publishedUrlProvider)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    public async Task<IActionResult> SwishPayment([FromQuery(Name = "ref")] Guid? reference)
    {
        ViewData["SwishPayment"] = await LoadAsync(reference);

        // The sidebar shows the member their current bookings here too, so paying does not mean
        // losing sight of what they already have booked.
        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        ViewData["MemberBookings"] = user is null
            ? Array.Empty<MemberBookingRow>()
            : await bookingsProvider.GetCurrentAsync(user.Key);

        return CurrentTemplate(CurrentPage);
    }

    private async Task<SwishPaymentViewModel?> LoadAsync(Guid? reference)
    {
        if (reference is null)
        {
            return null;
        }

        MemberIdentityUser? user = await memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            return null;
        }

        PaymentRecord? payment = await repository.GetPaymentByReferenceAsync(reference.Value);

        // The ownership check. Without it, guessing or sharing a reference would let anyone view -
        // and, through the actions below, settle - somebody else's payment. Treated as "not found"
        // rather than "forbidden" so a reference cannot be probed for existence.
        if (payment is null || payment.MemberKey != user.Key)
        {
            logger.LogWarning("A payment reference was requested by someone who does not own it.");
            return null;
        }

        BookingRecord? booking = payment.BookingId is { } bookingId
            ? await repository.GetBookingAsync(bookingId)
            : null;

        TrainingClass? trainingClass = booking is null ? null : classes.Find(booking.ClassKey);

        var isMock = paymentProvider.Name == SwishMockPaymentProvider.ProviderName;
        var isStarted = payment.ProviderReference is not null;

        // The page itself is where the Swish app sends the member back to, so it is the return URL.
        var pageUrl = PaymentPageUrl.For(contentQuery, publishedUrlProvider, payment.Reference);
        var absolutePageUrl = pageUrl is null
            ? null
            : new Uri(new Uri($"{Request.Scheme}://{Request.Host}"), pageUrl).ToString();

        var appLink = isStarted && !isMock && payment.ProviderToken is not null && absolutePageUrl is not null
            ? SwishRequest.AppLink(payment.ProviderToken, absolutePageUrl)
            : null;

        var query = $"?reference={Uri.EscapeDataString(payment.Reference.ToString())}";

        string? outcomeMessage = payment.Status switch
        {
            PaymentStatus.Failed => SwishOutcome.Resolve(SwishOutcome.Error, payment.ErrorCode).MemberMessage,
            PaymentStatus.Cancelled => SwishOutcome.Resolve(SwishOutcome.Cancelled, null).MemberMessage,
            _ => null,
        };

        return new SwishPaymentViewModel(
            payment.Reference,
            payment.AmountOre,
            payment.MembershipFeeOre,
            payment.FamilyFeeOre,
            payment.ClassFeeOre,
            payment.BookingId,
            trainingClass?.Title,
            booking?.ClassStartUtc,
            payment.Status,
            booking?.Status,
            booking?.HoldExpiresUtc,
            isMock,
            isStarted,
            appLink,
            QrUrl: isStarted && !isMock
                ? Url.SurfaceAction(
                    nameof(SwishPaymentSurfaceController.Qr),
                    ControllerExtensions.GetControllerName<SwishPaymentSurfaceController>()) + query
                : null,
            StatusUrl: isStarted
                ? Url.SurfaceAction(
                    nameof(SwishPaymentSurfaceController.Status),
                    ControllerExtensions.GetControllerName<SwishPaymentSurfaceController>()) + query
                : null,
            outcomeMessage);
    }
}
