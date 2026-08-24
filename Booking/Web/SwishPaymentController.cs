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

    /// <summary>Whole minutes left on the reservation, floored at zero.</summary>
    public int MinutesLeft => HoldExpiresUtc is { } expires
        ? Math.Max(0, (int)(expires - DateTime.UtcNow).TotalMinutes)
        : 0;
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
    Services.TrainingClassService classes)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    public async Task<IActionResult> SwishPayment([FromQuery(Name = "ref")] Guid? reference)
    {
        ViewData["SwishPayment"] = await LoadAsync(reference);
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
