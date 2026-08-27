using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class DurationFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static string Format(
        int? durationDays = null,
        DateTimeOffset? expires = null,
        StorageKind storage = StorageKind.Cookie,
        Locale locale = Locale.Sv)
        => DurationFormatter.Format(storage, durationDays, expires, Now, locale);

    // A cookie with no expiry dies with the browser session. So does one whose expiry has already
    // passed - a scan that catches a cookie mid-deletion must not declare it as lasting -3 days.
    [Fact]
    public void No_expiry_is_a_session_cookie()
    {
        Assert.Equal("Session", Format());
    }

    [Fact]
    public void An_expiry_in_the_past_is_a_session_cookie()
    {
        Assert.Equal("Session", Format(expires: Now.AddDays(-3)));
    }

    [Fact]
    public void A_catalogue_duration_of_zero_days_is_a_session_cookie()
    {
        Assert.Equal("Session", Format(durationDays: 0));
    }

    // localStorage has no expiry at all, and calling that "Session" would be a lie in the
    // visitor's favour - it survives closing the browser. That distinction is the whole reason
    // the policy page records a storage type.
    [Fact]
    public void Local_storage_lasts_until_it_is_deleted()
    {
        Assert.Equal("Tills den raderas", Format(storage: StorageKind.LocalStorage));
        Assert.Equal("Until deleted", Format(storage: StorageKind.LocalStorage, locale: Locale.En));
    }

    [Fact]
    public void Session_storage_is_a_session()
    {
        Assert.Equal("Session", Format(storage: StorageKind.SessionStorage));
    }

    [Fact]
    public void Under_a_day_reads_in_hours()
    {
        Assert.Equal("2 timmar", Format(expires: Now.AddHours(2)));
        Assert.Equal("2 hours", Format(expires: Now.AddHours(2), locale: Locale.En));
    }

    // Visitors read this text on a public page, so "1 timmar" is not acceptable output.
    [Fact]
    public void Singular_and_plural_forms_differ_in_both_locales()
    {
        Assert.Equal("1 timme", Format(expires: Now.AddHours(1)));
        Assert.Equal("1 hour", Format(expires: Now.AddHours(1), locale: Locale.En));
        Assert.Equal("1 dag", Format(durationDays: 1));
        Assert.Equal("1 day", Format(durationDays: 1, locale: Locale.En));
    }

    // No month singular is asserted above because none is reachable, and that is worth pinning
    // down rather than leaving as a surprise: the smallest value that reaches the months branch is
    // 60 days, which is 1.97 months and rounds to 2. Anything shorter renders in days by design.
    // The singular arm of the switch stays as defensive code. Lower MonthsFromDays to 45 if a
    // "1 månad" output is ever wanted.
    [Fact]
    public void The_smallest_month_output_is_two_because_of_the_sixty_day_threshold()
    {
        Assert.Equal("59 dagar", Format(durationDays: 59));
        Assert.Equal("2 månader", Format(durationDays: 60));
    }

    // Never "0 timmar". A cookie that expires in forty seconds still exists, and rounding it away
    // to zero would read as a mistake rather than as a very short lifetime.
    [Fact]
    public void A_sub_minute_expiry_floors_to_one_hour_rather_than_zero()
    {
        Assert.Equal("1 timme", Format(expires: Now.AddSeconds(40)));
    }

    [Fact]
    public void Between_one_day_and_sixty_reads_in_days()
    {
        Assert.Equal("30 dagar", Format(expires: Now.AddDays(30)));
        Assert.Equal("30 days", Format(expires: Now.AddDays(30), locale: Locale.En));
    }

    // 30.44 days per month, not 30, so a year does not come out as "12 månader och lite".
    [Fact]
    public void A_year_reads_as_twelve_months()
    {
        Assert.Equal("12 månader", Format(durationDays: 365));
        Assert.Equal("12 months", Format(durationDays: 365, locale: Locale.En));
    }

    [Fact]
    public void Two_years_reads_as_twenty_four_months()
    {
        Assert.Equal("24 månader", Format(durationDays: 730));
    }

    // The catalogue's documented lifetime beats whatever this one browser happened to report,
    // which may be truncated by the browser's own cap on cookie lifetimes.
    [Fact]
    public void A_catalogue_duration_overrides_the_observed_expiry()
    {
        Assert.Equal("24 månader", Format(durationDays: 730, expires: Now.AddDays(7)));
    }

    [Fact]
    public void Wording_differs_between_an_unknown_and_a_needs_review_cookie()
    {
        Assert.NotEqual(Wording.UnknownPurpose(Locale.Sv), Wording.NeedsReviewPurpose(Locale.Sv));
        Assert.NotEmpty(Wording.UnknownProvider(Locale.Sv));
        Assert.NotEmpty(Wording.UnknownProvider(Locale.En));
    }
}
