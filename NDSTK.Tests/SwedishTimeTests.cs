using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class SwedishTimeTests
{
    // Sweden is UTC+1 in winter and UTC+2 in summer. An editor typing 18:00 means 18:00 in
    // Sweden both times, so the UTC instant must differ by season - this is the bug that would
    // otherwise send every July reminder an hour early.
    [Fact]
    public void ToUtc_in_winter_subtracts_one_hour()
    {
        var result = SwedishTime.ToUtc(new DateTime(2026, 1, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 1, 15, 17, 0, 0, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ToUtc_in_summer_subtracts_two_hours()
    {
        var result = SwedishTime.ToUtc(new DateTime(2026, 7, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToSwedish_round_trips_a_summer_instant()
    {
        var utc = SwedishTime.ToUtc(new DateTime(2026, 7, 15, 18, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 18, 0, 0), SwedishTime.ToSwedish(utc));
    }

    // A value that already claims to be UTC must not be shifted a second time.
    [Fact]
    public void ToUtc_leaves_an_instant_already_marked_utc_alone()
    {
        var utc = new DateTime(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal(utc, SwedishTime.ToUtc(utc));
    }
}
