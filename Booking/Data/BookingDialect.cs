using NDSTK.Booking.Domain;
using Umbraco.Cms.Infrastructure.Persistence;

namespace NDSTK.Booking.Data;

/// <summary>
/// Which engine the site is actually on. Local development runs SQLite; production runs SQL Server,
/// which is not visible from appsettings.json - the connection string is overridden there.
/// </summary>
internal static class BookingDialect
{
    /// <summary>
    /// Matched on the provider name rather than an Umbraco constant, because the constants live in
    /// the per-engine persistence packages this project does not reference directly. The values are
    /// "Microsoft.Data.Sqlite" and "Microsoft.Data.SqlClient".
    /// </summary>
    internal static SqlDialect Of(IUmbracoDatabase database)
        => database.SqlContext.SqlSyntax.DbProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? SqlDialect.Sqlite
            : SqlDialect.SqlServer;
}
