using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Infrastructure.Migrations;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// The columns a real Swish payment leaves behind on the payment row, and the index the callback
/// looks a payment up by.
/// </summary>
/// <remarks>
/// Every column is nullable: rows from before this step, and rows the member never starts a
/// payment on, have nothing to put in them. The index is filtered to rows that have a value,
/// because SQL Server treats NULLs as equal in a unique index and would refuse the second unstarted
/// payment. Created here rather than by an attribute on the POCO so a fresh install gets the same
/// filtered index as an upgraded one.
/// </remarks>
internal sealed class AddSwishColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        SqlDialect dialect = BookingDialect.Of(Database);
        var table = BookingTables.Payment;

        AddColumnIfMissing(table, "ProviderReference",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ProviderReference", 36));
        AddColumnIfMissing(table, "ProviderToken",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ProviderToken", 64));
        AddColumnIfMissing(table, "CallbackIdentifier",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "CallbackIdentifier", 36));
        AddColumnIfMissing(table, "BankReference",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "BankReference", 64));
        AddColumnIfMissing(table, "ErrorCode",
            BookingSchemaSql.AddNullableStringColumn(dialect, table, "ErrorCode", 20));
        AddColumnIfMissing(table, "StartedUtc",
            BookingSchemaSql.AddNullableDateTimeColumn(dialect, table, "StartedUtc"));
        AddColumnIfMissing(table, "LastCheckedUtc",
            BookingSchemaSql.AddNullableDateTimeColumn(dialect, table, "LastCheckedUtc"));

        CreateIndexIfMissing(dialect);

        return Task.CompletedTask;
    }

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

    private void CreateIndexIfMissing(SqlDialect dialect)
    {
        var exists = Database.ExecuteScalar<int>(
            BookingSchemaSql.IndexExistsQuery(dialect), BookingTables.PaymentProviderReferenceIndex) > 0;

        if (exists)
        {
            Logger.LogDebug(
                "Index {IndexName} already exists; skipping.", BookingTables.PaymentProviderReferenceIndex);
            return;
        }

        Database.Execute(BookingSchemaSql.CreateFilteredUniqueIndex(
            BookingTables.PaymentProviderReferenceIndex, BookingTables.Payment, "ProviderReference"));

        Logger.LogInformation("Created index {IndexName}.", BookingTables.PaymentProviderReferenceIndex);
    }
}
