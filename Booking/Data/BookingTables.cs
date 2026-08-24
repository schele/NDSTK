namespace NDSTK.Booking.Data;

/// <summary>Table names, in one place so the POCOs and the migration cannot disagree.</summary>
internal static class BookingTables
{
    internal const string Booking = "ndstkBooking";
    internal const string Payment = "ndstkPayment";
    internal const string Credit = "ndstkBookingCredit";
}
