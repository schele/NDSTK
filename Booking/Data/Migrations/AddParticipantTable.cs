using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Schema only: the participant table, and the two columns the backfill needs somewhere to write.
/// </summary>
/// <remarks>
/// Deliberately does NOT touch the one-live-booking index. Swapping it belongs with the backfill,
/// because creating IX_ndstkBooking_OneLivePerParticipantClass while every ParticipantKey is still
/// null goes wrong differently on each engine and never usefully: SQLite treats nulls as distinct
/// and silently produces an index that enforces nothing at all, so the overbooking guarantee would
/// be gone with no error raised; SQL Server treats nulls as equal and rejects the second row
/// outright. See NdstkParticipantBackfill, which does it after filling the column in.
/// </remarks>
internal sealed class AddParticipantTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (TableExists(BookingTables.Participant))
        {
            Logger.LogDebug("Table {TableName} already exists; skipping.", BookingTables.Participant);
        }
        else
        {
            Create.Table<ParticipantRecord>().Do();
            Logger.LogInformation("Created table {TableName}.", BookingTables.Participant);
        }

        SqlDialect dialect = BookingDialect.Of(Database);

        AddColumnIfMissing(
            BookingTables.Booking,
            "ParticipantKey",
            BookingSchemaSql.AddNullableGuidColumn(dialect, BookingTables.Booking, "ParticipantKey"));

        AddColumnIfMissing(
            BookingTables.Payment,
            "FamilyFeeOre",
            BookingSchemaSql.AddIntegerColumn(dialect, BookingTables.Payment, "FamilyFeeOre", 0));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Neither engine has ADD COLUMN IF NOT EXISTS, and the expression builder throws when the
    /// column is already there, so the column is checked for first. Raw SQL rather than Alter.Table
    /// because the DEFAULT is what keeps the NOT NULL satisfiable on rows that already exist.
    /// </summary>
    private void AddColumnIfMissing(string table, string column, string sql)
    {
        if (ColumnExists(table, column))
        {
            Logger.LogDebug("Column {Table}.{Column} already exists; skipping.", table, column);
            return;
        }

        Database.Execute(sql);
        Logger.LogInformation("Added column {Table}.{Column}.", table, column);
    }
}
