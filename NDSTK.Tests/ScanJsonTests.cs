using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ScanJsonTests
{
    private static readonly Guid PolicyPageKey = new("11111111-2222-3333-4444-555555555555");

    private static ScanResult Sample() => new(
        Candidates:
        [
            new("_ga_*", "Google Analytics", "statistics", "Mäter.", "24 månader", "Cookie",
                CandidateFlag.NeedsReview, ConsentPass.AcceptAll, "https://ndstk.se/"),
        ],
        Violations:
        [
            new("_fbp", "Meta", "marketing", "Annonser.", "3 månader", "Cookie",
                CandidateFlag.Violation, ConsentPass.RejectAll, "https://ndstk.se/x"),
        ],
        ExpectedButNotObserved: ["UMB_MEMBER"],
        HostsByPass: new Dictionary<ConsentPass, IReadOnlyList<string>>
        {
            [ConsentPass.AcceptAll] = ["www.google-analytics.com"],
        },
        Outcome: new MergeOutcome(["_ga_*"], ["ndstk-consent"], ["old"], PolicyPageKey, true),
        CanReachApi: true,
        DryRun: false,
        CompletedAt: new DateTimeOffset(2026, 8, 28, 9, 30, 0, TimeSpan.Zero),
        Site: "https://ndstk.se/");

    // The history browser loads past scans back into the same grid a live scan fills, so a report
    // that cannot be read back is a report history cannot use.
    [Fact]
    public void A_result_survives_a_round_trip_intact()
    {
        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(Sample()));

        Assert.NotNull(back);
        Assert.Equal("https://ndstk.se/", back.Site);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 9, 30, 0, TimeSpan.Zero), back.CompletedAt);
        Assert.Single(back.Candidates);
        Assert.Equal("_ga_*", back.Candidates[0].Name);
        Assert.Equal("Mäter.", back.Candidates[0].Purpose);
        Assert.Equal(CandidateFlag.NeedsReview, back.Candidates[0].Flag);
        Assert.Single(back.Violations);
        Assert.Equal(ConsentPass.RejectAll, back.Violations[0].FirstSeenPass);
        Assert.Equal(["UMB_MEMBER"], back.ExpectedButNotObserved);
        Assert.True(back.Outcome!.Saved);
        Assert.Equal(["_ga_*"], back.Outcome.Added);

        // The policy page key is the one field a history entry needs to identify what it wrote
        // to, so it must survive the round trip like everything else.
        Assert.Equal(PolicyPageKey, back.Outcome.PolicyPageKey);
        Assert.True(back.CanReachApi);
    }

    // The hosts dictionary is keyed by an enum. Without a converter it serialises as an integer
    // key on one side and a name on the other, which is how the pre-UI report ended up encoding
    // ConsentPass two different ways in the same file.
    [Fact]
    public void The_hosts_dictionary_round_trips_with_its_enum_key()
    {
        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(Sample()));

        Assert.True(back!.HostsByPass.ContainsKey(ConsentPass.AcceptAll));
        Assert.Contains("www.google-analytics.com", back.HostsByPass[ConsentPass.AcceptAll]);
    }

    // Enums are written as names, not integers, so the file is readable by a human and stable if
    // an enum member is ever reordered.
    [Fact]
    public void Enums_are_written_as_names()
    {
        string json = ScanJson.Serialize(Sample());

        Assert.Contains("RejectAll", json);
        Assert.DoesNotContain("\"firstSeenPass\": 1", json);
    }

    // History skips a file it cannot parse rather than failing the whole list, so Deserialize must
    // return null instead of throwing.
    [Fact]
    public void Unparseable_json_returns_null_rather_than_throwing()
    {
        Assert.Null(ScanJson.Deserialize("this is not json"));
        Assert.Null(ScanJson.Deserialize("[]"));
    }
}
