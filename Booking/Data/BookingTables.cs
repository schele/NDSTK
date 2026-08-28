namespace NDSTK.Booking.Data;

/// <summary>
/// Table and index names, in one place so the POCOs, the migration and the backfill cannot
/// disagree. The two index names are here rather than private to one file precisely because three
/// files have to name the same index for the swap to work.
/// </summary>
internal static class BookingTables
{
    internal const string Booking = "ndstkBooking";
    internal const string Payment = "ndstkPayment";
    internal const string Credit = "ndstkBookingCredit";
    internal const string Participant = "ndstkParticipant";

    /// <summary>The original rule: one live booking per account per class.</summary>
    internal const string LivePerMemberIndex = "IX_ndstkBooking_OneLivePerMemberClass";

    /// <summary>The rule after the backfill: one live booking per child per class.</summary>
    internal const string LivePerParticipantIndex = "IX_ndstkBooking_OneLivePerParticipantClass";
}
