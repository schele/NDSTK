using System.Globalization;

namespace NDSTK.Booking.Web;

/// <summary>
/// The eight-digit ÅÅÅÅMMDD form a Swedish parent will type without being asked, because it is the
/// first eight digits of a personnummer.
/// </summary>
/// <remarks>
/// Only the date is ever taken. No personnummer is collected or stored, so a twelve-digit value is
/// rejected rather than silently truncated - accepting it would invite people to type it.
/// </remarks>
public static class SwedishDate
{
    public static bool TryParseCompact(string? value, out DateOnly date)
    {
        date = default;

        var trimmed = value?.Trim();

        // The length check is what rejects a full personnummer. TryParseExact is what rejects
        // "2026ab01" and impossible dates such as 20261301.
        return trimmed is { Length: 8 }
               && DateOnly.TryParseExact(
                   trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static string ToCompact(DateOnly date)
        => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whole years, counted on a given day rather than today, so a class roster can age a child on
    /// the date the class actually runs.
    /// </summary>
    public static int AgeOn(DateOnly birthDate, DateOnly on)
    {
        var age = on.Year - birthDate.Year;
        return birthDate > on.AddYears(-age) ? age - 1 : age;
    }
}
