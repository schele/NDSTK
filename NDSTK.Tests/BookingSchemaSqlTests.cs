using Microsoft.Data.Sqlite;
using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// The booking schema statements the expression builder cannot express. These were written for
/// SQLite only, which took the whole migration down on SQL Server: the CREATE INDEX threw, Umbraco
/// rolled the scope back, and the tables it had just created went with it.
/// </summary>
public class BookingSchemaSqlTests
{
    private const string Booking = "ndstkBooking";
    private const string Payment = "ndstkPayment";
    private const string Participant = "ndstkParticipant";
    private const string Index = "IX_ndstkBooking_OneLivePerParticipantClass";

    [Fact]
    public void The_live_booking_index_never_says_IF_NOT_EXISTS()
    {
        // The regression itself. SQL Server has no IF NOT EXISTS on CREATE INDEX, so callers have
        // to ask IndexExistsQuery first instead.
        var sql = BookingSchemaSql.CreateLiveBookingIndex(Index, Booking, "ParticipantKey");

        Assert.DoesNotContain("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_live_booking_index_counts_only_the_two_live_statuses()
    {
        var sql = BookingSchemaSql.CreateLiveBookingIndex(Index, Booking, "ParticipantKey");

        Assert.Contains($"'{BookingStatus.Pending}'", sql, StringComparison.Ordinal);
        Assert.Contains($"'{BookingStatus.Confirmed}'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{BookingStatus.Cancelled}'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{BookingStatus.Expired}'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropping_an_index_on_sql_server_names_the_table()
    {
        // T-SQL is DROP INDEX IF EXISTS <name> ON <table>; without the table it does not parse.
        Assert.Equal(
            $"DROP INDEX IF EXISTS {Index} ON {Booking}",
            BookingSchemaSql.DropIndex(SqlDialect.SqlServer, Index, Booking));
    }

    [Fact]
    public void Dropping_an_index_on_sqlite_does_not_name_the_table()
    {
        Assert.Equal(
            $"DROP INDEX IF EXISTS {Index}",
            BookingSchemaSql.DropIndex(SqlDialect.Sqlite, Index, Booking));
    }

    [Fact]
    public void A_nullable_guid_column_uses_each_engines_own_type_and_keyword()
    {
        Assert.Equal(
            $"ALTER TABLE {Booking} ADD ParticipantKey uniqueidentifier NULL",
            BookingSchemaSql.AddNullableGuidColumn(SqlDialect.SqlServer, Booking, "ParticipantKey"));

        Assert.Equal(
            $"ALTER TABLE {Booking} ADD COLUMN ParticipantKey TEXT NULL",
            BookingSchemaSql.AddNullableGuidColumn(SqlDialect.Sqlite, Booking, "ParticipantKey"));
    }

    [Fact]
    public void An_integer_column_carries_its_default_so_existing_rows_stay_valid()
    {
        Assert.Equal(
            $"ALTER TABLE {Payment} ADD FamilyFeeOre int NOT NULL DEFAULT 0",
            BookingSchemaSql.AddIntegerColumn(SqlDialect.SqlServer, Payment, "FamilyFeeOre", 0));

        Assert.Equal(
            $"ALTER TABLE {Payment} ADD COLUMN FamilyFeeOre INTEGER NOT NULL DEFAULT 0",
            BookingSchemaSql.AddIntegerColumn(SqlDialect.Sqlite, Payment, "FamilyFeeOre", 0));
    }

    [Fact]
    public void A_nullable_string_column_uses_nvarchar_on_sql_server_and_text_on_sqlite()
    {
        Assert.Equal(
            $"ALTER TABLE {Payment} ADD ProviderReference nvarchar(36) NULL",
            BookingSchemaSql.AddNullableStringColumn(SqlDialect.SqlServer, Payment, "ProviderReference", 36));

        Assert.Equal(
            $"ALTER TABLE {Payment} ADD COLUMN ProviderReference TEXT NULL",
            BookingSchemaSql.AddNullableStringColumn(SqlDialect.Sqlite, Payment, "ProviderReference", 36));
    }

    [Fact]
    public void A_nullable_datetime_column_matches_the_types_umbraco_already_used()
    {
        Assert.Equal(
            $"ALTER TABLE {Payment} ADD StartedUtc datetime NULL",
            BookingSchemaSql.AddNullableDateTimeColumn(SqlDialect.SqlServer, Payment, "StartedUtc"));

        Assert.Equal(
            $"ALTER TABLE {Payment} ADD COLUMN StartedUtc TEXT NULL",
            BookingSchemaSql.AddNullableDateTimeColumn(SqlDialect.Sqlite, Payment, "StartedUtc"));
    }

    [Fact]
    public void The_filtered_unique_index_excludes_nulls_and_never_says_IF_NOT_EXISTS()
    {
        var sql = BookingSchemaSql.CreateFilteredUniqueIndex(
            "IX_ndstkPayment_ProviderReference", Payment, "ProviderReference");

        Assert.Equal(
            $"CREATE UNIQUE INDEX IX_ndstkPayment_ProviderReference ON {Payment} (ProviderReference) "
            + "WHERE ProviderReference IS NOT NULL",
            sql);
        Assert.DoesNotContain("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_filtered_unique_index_lets_many_unstarted_payments_coexist_but_not_two_of_one_request()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Execute(connection,
            $"CREATE TABLE {Payment} (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProviderReference TEXT NULL)");
        Execute(connection, BookingSchemaSql.CreateFilteredUniqueIndex(
            "IX_ndstkPayment_ProviderReference", Payment, "ProviderReference"));

        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES (NULL)");
        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES (NULL)");
        Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES ('ABC')");

        SqliteException failure = Assert.Throws<SqliteException>(
            () => Execute(connection, $"INSERT INTO {Payment} (ProviderReference) VALUES ('ABC')"));

        Assert.Contains("UNIQUE", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Each_engine_is_asked_about_indexes_in_the_catalogue_it_actually_has()
    {
        Assert.Contains("sqlite_master", BookingSchemaSql.IndexExistsQuery(SqlDialect.Sqlite));
        Assert.Contains("sys.indexes", BookingSchemaSql.IndexExistsQuery(SqlDialect.SqlServer));
    }

    // --- executed against a real engine, so the statement is proven to parse and to enforce ---

    [Fact]
    public void The_index_stops_one_participant_taking_two_places_on_one_class()
    {
        using SqliteConnection connection = OpenBookingDatabase();
        Guid participant = Guid.NewGuid();
        Guid theClass = Guid.NewGuid();

        Insert(connection, participant, theClass, BookingStatus.Pending);

        SqliteException failure = Assert.Throws<SqliteException>(
            () => Insert(connection, participant, theClass, BookingStatus.Confirmed));

        Assert.Contains("UNIQUE", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_index_still_lets_a_participant_rebook_a_class_they_cancelled()
    {
        using SqliteConnection connection = OpenBookingDatabase();
        Guid participant = Guid.NewGuid();
        Guid theClass = Guid.NewGuid();

        Insert(connection, participant, theClass, BookingStatus.Cancelled);
        Insert(connection, participant, theClass, BookingStatus.Confirmed);

        Assert.Equal(2, Count(connection));
    }

    [Fact]
    public void The_index_leaves_two_siblings_free_to_take_the_same_class()
    {
        using SqliteConnection connection = OpenBookingDatabase();
        Guid theClass = Guid.NewGuid();

        Insert(connection, Guid.NewGuid(), theClass, BookingStatus.Confirmed);
        Insert(connection, Guid.NewGuid(), theClass, BookingStatus.Confirmed);

        Assert.Equal(2, Count(connection));
    }

    [Fact]
    public void Pointing_bookings_at_participants_uses_each_engines_own_row_limit()
    {
        var sqlServer = BookingSchemaSql.PointBookingsAtParticipants(
            SqlDialect.SqlServer, Booking, Participant);
        Assert.Contains("TOP 1", sqlServer, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT", sqlServer, StringComparison.OrdinalIgnoreCase);

        var sqlite = BookingSchemaSql.PointBookingsAtParticipants(
            SqlDialect.Sqlite, Booking, Participant);
        Assert.Contains("LIMIT 1", sqlite, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP", sqlite, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SqlDialect.Sqlite)]
    [InlineData(SqlDialect.SqlServer)]
    public void Pointing_bookings_at_participants_quotes_the_reserved_word_Key(SqlDialect dialect)
    {
        // KEY is reserved in T-SQL, so an unquoted p.Key does not parse there.
        var sql = BookingSchemaSql.PointBookingsAtParticipants(dialect, Booking, Participant);

        Assert.Contains("[Key]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Pointing_bookings_at_participants_picks_the_oldest_participant_on_the_account()
    {
        using SqliteConnection connection = OpenBookingDatabase();
        Guid member = Guid.NewGuid();
        Guid oldest = Guid.NewGuid();
        Guid younger = Guid.NewGuid();

        InsertParticipant(connection, oldest, member);
        InsertParticipant(connection, younger, member);
        InsertUnpointedBooking(connection, member, Guid.NewGuid(), BookingStatus.Confirmed);

        Execute(connection, BookingSchemaSql.PointBookingsAtParticipants(
            SqlDialect.Sqlite, Booking, Participant));

        Assert.Equal(oldest.ToString(), Scalar(connection, $"SELECT ParticipantKey FROM {Booking}"));
    }

    private static SqliteConnection OpenBookingDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Execute(connection,
            $"CREATE TABLE {Booking} (Id INTEGER PRIMARY KEY AUTOINCREMENT, MemberKey TEXT NULL, "
            + "ParticipantKey TEXT NULL, ClassKey TEXT NOT NULL, Status TEXT NOT NULL)");

        Execute(connection,
            $"CREATE TABLE {Participant} (Id INTEGER PRIMARY KEY AUTOINCREMENT, "
            + "[Key] TEXT NOT NULL, MemberKey TEXT NOT NULL)");

        Execute(connection, BookingSchemaSql.CreateLiveBookingIndex(Index, Booking, "ParticipantKey"));

        return connection;
    }

    private static void Insert(SqliteConnection connection, Guid participant, Guid theClass, string status)
        => Execute(connection,
            $"INSERT INTO {Booking} (ParticipantKey, ClassKey, Status) "
            + $"VALUES ('{participant}', '{theClass}', '{status}')");

    private static void InsertParticipant(SqliteConnection connection, Guid key, Guid member)
        => Execute(connection,
            $"INSERT INTO {Participant} ([Key], MemberKey) VALUES ('{key}', '{member}')");

    /// <summary>A booking from before participants existed: it knows the account, not the child.</summary>
    private static void InsertUnpointedBooking(
        SqliteConnection connection, Guid member, Guid theClass, string status)
        => Execute(connection,
            $"INSERT INTO {Booking} (MemberKey, ParticipantKey, ClassKey, Status) "
            + $"VALUES ('{member}', NULL, '{theClass}', '{status}')");

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static int Count(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {Booking}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
