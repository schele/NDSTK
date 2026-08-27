using System.Globalization;

namespace NDSTK.CookieScan.Core;

/// <summary>
/// Turns a documented lifetime or an observed expiry into the sentence a visitor reads in the
/// duration column of the cookie policy table.
/// </summary>
public static class DurationFormatter
{
    // Mean days per month. 30 would render a 365-day cookie as 12.17 months and a 730-day one as
    // 24.3, so the two commonest real lifetimes would both round wrong.
    private const double DaysPerMonth = 30.44;

    // Below this, days read better than months: "45 dagar" is clearer than "1 månad".
    private const int MonthsFromDays = 60;

    /// <summary>
    /// The duration text. <paramref name="durationDays"/> is the catalogue's documented lifetime
    /// and wins when present - a browser may cap or truncate what it reports.
    /// <c>0</c> means a session cookie.
    /// </summary>
    public static string Format(
        StorageKind storage,
        int? durationDays,
        DateTimeOffset? expires,
        DateTimeOffset now,
        Locale locale)
    {
        // Storage kind decides before any lifetime does: neither of these has an expiry to read,
        // and localStorage outliving the session is the fact worth telling a visitor.
        if (storage == StorageKind.LocalStorage)
        {
            return locale == Locale.Sv ? "Tills den raderas" : "Until deleted";
        }

        if (storage == StorageKind.SessionStorage)
        {
            return Session();
        }

        if (durationDays is int documented)
        {
            return documented <= 0 ? Session() : FromDays(documented, locale);
        }

        if (expires is null || expires <= now)
        {
            return Session();
        }

        TimeSpan span = expires.Value - now;

        if (span.TotalHours < 24)
        {
            return Plural(AtLeastOne(span.TotalHours), Unit.Hour, locale);
        }

        return FromDays(AtLeastOne(span.TotalDays), locale);

        string Session() => "Session";
    }

    private static string FromDays(int days, Locale locale)
        => days < MonthsFromDays
            ? Plural(days, Unit.Day, locale)
            : Plural(AtLeastOne(days / DaysPerMonth), Unit.Month, locale);

    // Rounds to the nearest whole unit but never to zero: a cookie expiring in forty seconds
    // still exists, and "0 timmar" reads as a bug rather than as a very short lifetime.
    private static int AtLeastOne(double value) => Math.Max(1, (int)Math.Round(value));

    private static string Plural(int count, Unit unit, Locale locale)
    {
        string word = (unit, locale, count) switch
        {
            (Unit.Hour, Locale.Sv, 1) => "timme",
            (Unit.Hour, Locale.Sv, _) => "timmar",
            (Unit.Hour, _, 1) => "hour",
            (Unit.Hour, _, _) => "hours",
            (Unit.Day, Locale.Sv, 1) => "dag",
            (Unit.Day, Locale.Sv, _) => "dagar",
            (Unit.Day, _, 1) => "day",
            (Unit.Day, _, _) => "days",
            (Unit.Month, Locale.Sv, 1) => "månad",
            (Unit.Month, Locale.Sv, _) => "månader",
            (Unit.Month, _, 1) => "month",
            _ => "months",
        };

        return string.Create(CultureInfo.InvariantCulture, $"{count} {word}");
    }

    private enum Unit
    {
        Hour,
        Day,
        Month,
    }
}
