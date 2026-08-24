using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using NDSTK.Booking.Data;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;

namespace NDSTK.Booking.Web;

/// <summary>What the payment page should render.</summary>
public sealed record SwishPaymentViewModel(
    Guid Reference,
    int AmountOre,
    int MembershipFeeOre,
    int ClassFeeOre,
    string? ClassTitle,
    DateTime? ClassStartUtc,
    string Status,
    DateTime? HoldExpiresUtc)
{
    public bool IsPending => Status == PaymentStatus.Pending;
    public bool IsPaid => Status == PaymentStatus.Paid;

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
    MemberBookingsProvider bookingsProvider)
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

        return new SwishPaymentViewModel(
            payment.Reference,
            payment.AmountOre,
            payment.MembershipFeeOre,
            payment.ClassFeeOre,
            trainingClass?.Title,
            booking?.ClassStartUtc,
            payment.Status,
            booking?.HoldExpiresUtc);
    }
}
