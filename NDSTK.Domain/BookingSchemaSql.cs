namespace NDSTK.Booking.Domain;

/// <summary>The engine a statement is being written for.</summary>
public enum SqlDialect
{
    Sqlite,
    SqlServer,
}

/// <summary>
/// The few booking schema statements Umbraco's expression builder cannot express, written for both
/// engines this site runs on: SQLite locally, SQL Server in production.
/// </summary>
/// <remarks>
/// Here rather than next to the migrations because it is the SQL half of a domain rule -
/// <see cref="Capacity.HasLiveBooking"/> is the C# half, and the two have to agree about which
/// statuses hold a place. Keeping them in one project makes disagreeing harder, and keeps this
/// testable without the web assembly.
///
/// Everything is emitted as a single line so a test can assert on the exact statement.
/// </remarks>
public static class BookingSchemaSql
{
    /// <summary>
    /// A partial (SQLite) or filtered (SQL Server) unique index: at most one live booking per key
    /// per class, while leaving cancelled and expired rows out of the index entirely so the class
    /// can be booked again. Both engines accept this statement verbatim.
    /// </summary>
    /// <remarks>
    /// Deliberately no IF NOT EXISTS. SQLite accepts it, SQL Server has no such clause on CREATE
    /// INDEX and fails to parse the whole statement - which is what took the booking tables down,
    /// because Umbraco rolls the migration scope back and the CREATE TABLEs went with it. Ask
    /// <see cref="IndexExistsQuery"/> first instead.
    /// </remarks>
    public static string CreateLiveBookingIndex(string indexName, string table, string keyColumn)
        => $"CREATE UNIQUE INDEX {indexName} ON {table} ({keyColumn}, ClassKey) "
            + $"WHERE Status IN ('{BookingStatus.Pending}', '{BookingStatus.Confirmed}')";

    /// <summary>
    /// T-SQL requires the table an index belongs to; SQLite has one index namespace per database
    /// and rejects the ON clause.
    /// </summary>
    public static string DropIndex(SqlDialect dialect, string indexName, string table)
        => dialect is SqlDialect.SqlServer
            ? $"DROP INDEX IF EXISTS {indexName} ON {table}"
            : $"DROP INDEX IF EXISTS {indexName}";

    /// <summary>
    /// Counts indexes of a given name, taking it as parameter @0. Stands in for the IF NOT EXISTS
    /// that <see cref="CreateLiveBookingIndex"/> cannot use.
    /// </summary>
    public static string IndexExistsQuery(SqlDialect dialect)
        => dialect is SqlDialect.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE name = @0"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @0";

    /// <summary>
    /// A nullable key column. SQLite stores a Guid as TEXT; SQL Server has a real type for it, and
    /// picking TEXT there would silently change how the column sorts and compares.
    /// </summary>
    public static string AddNullableGuidColumn(SqlDialect dialect, string table, string column)
        => AddColumn(dialect, table, column,
            dialect is SqlDialect.SqlServer ? "uniqueidentifier NULL" : "TEXT NULL");

    /// <summary>
    /// The default is what keeps NOT NULL satisfiable on rows that already exist; both engines
    /// backfill it as part of the ALTER.
    /// </summary>
    public static string AddIntegerColumn(SqlDialect dialect, string table, string column, int defaultValue)
        => AddColumn(dialect, table, column,
            $"{(dialect is SqlDialect.SqlServer ? "int" : "INTEGER")} NOT NULL DEFAULT {defaultValue}");

    /// <summary>
    /// A nullable text column of bounded length. SQL Server gets the length; SQLite has no
    /// bounded text type and takes TEXT, which is also what NPoco reads a string back from.
    /// </summary>
    public static string AddNullableStringColumn(SqlDialect dialect, string table, string column, int length)
        => AddColumn(dialect, table, column,
            dialect is SqlDialect.SqlServer ? $"nvarchar({length}) NULL" : "TEXT NULL");

    /// <summary>
    /// A nullable datetime. Umbraco's own syntax providers created the existing date columns as
    /// datetime on SQL Server and TEXT on SQLite, and NPoco formats every value it writes the
    /// same way for both, so the new columns sort and compare like the old ones.
    /// </summary>
    public static string AddNullableDateTimeColumn(SqlDialect dialect, string table, string column)
        => AddColumn(dialect, table, column,
            dialect is SqlDialect.SqlServer ? "datetime NULL" : "TEXT NULL");

    /// <summary>
    /// Unique among the rows that have a value. Without the filter SQL Server treats every NULL
    /// as the same value and refuses the second payment that has not started; SQLite would
    /// accept it, and the two engines would enforce different rules. Both accept this statement
    /// verbatim. No IF NOT EXISTS, for the reason <see cref="CreateLiveBookingIndex"/> gives.
    /// </summary>
    public static string CreateFilteredUniqueIndex(string indexName, string table, string column)
        => $"CREATE UNIQUE INDEX {indexName} ON {table} ({column}) WHERE {column} IS NOT NULL";

    /// <summary>
    /// Points every booking that predates participants at the oldest participant on its account.
    /// Each such booking belonged to an account that had exactly one participant a moment earlier,
    /// so the oldest is unambiguously the right one.
    /// </summary>
    /// <remarks>
    /// Two dialect differences in one statement. SQLite takes LIMIT after the ORDER BY, T-SQL takes
    /// TOP before the column list and has no LIMIT at all. And KEY is a reserved word in T-SQL, so
    /// the column has to be quoted - SQLite accepts the same bracket quoting, so one spelling
    /// serves both.
    /// </remarks>
    public static string PointBookingsAtParticipants(
        SqlDialect dialect, string bookingTable, string participantTable)
    {
        var oldestOnTheAccount = dialect is SqlDialect.SqlServer
            ? $"SELECT TOP 1 p.[Key] FROM {participantTable} p "
                + $"WHERE p.MemberKey = {bookingTable}.MemberKey ORDER BY p.Id"
            : $"SELECT p.[Key] FROM {participantTable} p "
                + $"WHERE p.MemberKey = {bookingTable}.MemberKey ORDER BY p.Id LIMIT 1";

        return $"UPDATE {bookingTable} SET ParticipantKey = ({oldestOnTheAccount}) "
            + "WHERE ParticipantKey IS NULL";
    }

    /// <summary>T-SQL spells it ADD; SQLite insists on ADD COLUMN.</summary>
    private static string AddColumn(SqlDialect dialect, string table, string column, string definition)
        => dialect is SqlDialect.SqlServer
            ? $"ALTER TABLE {table} ADD {column} {definition}"
            : $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
}
