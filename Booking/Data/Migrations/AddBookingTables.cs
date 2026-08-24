using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Creates the three booking tables. Note AsyncMigrationBase, not MigrationBase - the latter does
/// not exist in Umbraco 18.
/// </summary>
internal sealed class AddBookingTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        CreateIfMissing<BookingRecord>(BookingTables.Booking);
        CreateIfMissing<PaymentRecord>(BookingTables.Payment);
        CreateIfMissing<CreditRecord>(BookingTables.Credit);

        // A member may hold at most one live booking per class. Expressed as a partial unique
        // index so a cancelled booking does not block rebooking the same class. The expression
        // builder has no partial-index support, hence raw SQL - SQLite has supported this since
        // 3.8, and this site runs SQLite.
        Database.Execute($"""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ndstkBooking_OneLivePerMemberClass
            ON {BookingTables.Booking} (MemberKey, ClassKey)
            WHERE Status IN ('{BookingStatus.Pending}', '{BookingStatus.Confirmed}')
            """);

        return Task.CompletedTask;
    }

    private void CreateIfMissing<T>(string tableName)
    {
        if (TableExists(tableName))
        {
            Logger.LogDebug("Table {TableName} already exists; skipping.", tableName);
            return;
        }

        Create.Table<T>().Do();
        Logger.LogInformation("Created table {TableName}.", tableName);
    }
}
