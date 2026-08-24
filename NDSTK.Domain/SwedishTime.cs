namespace NDSTK.Booking.Domain;

/// <summary>
/// Converts between the Swedish wall-clock time an editor types into the backoffice date picker
/// and the UTC instants stored in the booking tables. The date picker returns a value with no
/// offset, so without this every reminder would be one or two hours out depending on the season.
/// </summary>
public static class SwedishTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime ToUtc(DateTime swedishLocal)
    {
        if (swedishLocal.Kind == DateTimeKind.Utc)
        {
            return swedishLocal;
        }

        DateTime unspecified = DateTime.SpecifyKind(swedishLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public static DateTime ToSwedish(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>
    /// .NET 10 accepts IANA ids on Windows through ICU, and the web project opts in to app-local
    /// ICU. The Windows id is still tried as a fallback so a host with ICU disabled degrades to
    /// the right zone rather than throwing at startup.
    /// </summary>
    private static TimeZoneInfo ResolveZone()
    {
        foreach (string id in new[] { "Europe/Stockholm", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next id.
            }
        }

        throw new InvalidOperationException(
            "Neither 'Europe/Stockholm' nor 'W. Europe Standard Time' is available on this host.");
    }
}
