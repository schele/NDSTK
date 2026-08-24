using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// The booking feature's own migration plan. Its state is tracked separately from Umbraco's, so a
/// future schema change is a new step appended here rather than an edit to an existing migration.
/// </summary>
internal sealed class BookingMigrationPlan : MigrationPlan
{
    public BookingMigrationPlan() : base("NDSTK.Booking")
        => From(string.Empty)
            .To<AddBookingTables>("booking-tables-1")
            .To<AddParticipantTable>("participants-1");
}
