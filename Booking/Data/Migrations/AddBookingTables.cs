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

        CreateLiveBookingIndexIfMissing();

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

    /// <summary>
    /// A member may hold at most one live booking per class. Expressed as a partial unique index so
    /// a cancelled booking does not block rebooking the same class; the expression builder has no
    /// partial-index support, hence raw SQL.
    /// </summary>
    /// <remarks>
    /// Existence is checked here rather than with IF NOT EXISTS, which only SQLite has. Writing it
    /// the SQLite way made the whole statement fail to parse on SQL Server, and because a migration
    /// runs in a scope Umbraco only completes on success, the three CREATE TABLEs above rolled back
    /// with it - which is why production had no booking tables at all rather than a missing index.
    /// </remarks>
    private void CreateLiveBookingIndexIfMissing()
    {
        SqlDialect dialect = BookingDialect.Of(Database);

        var exists = Database.ExecuteScalar<int>(
            BookingSchemaSql.IndexExistsQuery(dialect), BookingTables.LivePerMemberIndex) > 0;

        if (exists)
        {
            Logger.LogDebug(
                "Index {IndexName} already exists; skipping.", BookingTables.LivePerMemberIndex);
            return;
        }

        Database.Execute(BookingSchemaSql.CreateLiveBookingIndex(
            BookingTables.LivePerMemberIndex, BookingTables.Booking, "MemberKey"));

        Logger.LogInformation("Created index {IndexName}.", BookingTables.LivePerMemberIndex);
    }
}
