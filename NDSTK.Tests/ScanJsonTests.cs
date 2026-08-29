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
        Site: "https://ndstk.se/",
        Options: null);

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

    // System.Text.Json fills a record's missing constructor parameters with default rather than
    // failing, so well-formed JSON of the wrong shape used to deserialize into a ScanResult whose
    // collections were all null - ExitCode then threw a NullReferenceException. An empty object is
    // the simplest instance of that shape.
    [Fact]
    public void An_empty_json_object_returns_null_rather_than_a_result_with_null_collections()
    {
        Assert.Null(ScanJson.Deserialize("{}"));
    }

    // A mistyped or wrong-cased key ("Candidate" for "candidates") is well-formed JSON that still
    // leaves a constructor parameter unfilled. This is the exact hand-edit mistake the shape check
    // exists to catch, not just a deliberately empty document.
    [Fact]
    public void Json_with_a_mistyped_key_returns_null_rather_than_a_result_with_a_null_collection()
    {
        const string json = """
            {
              "Candidate": [],
              "violations": [],
              "expectedButNotObserved": [],
              "hostsByPass": {},
              "canReachApi": false,
              "dryRun": false,
              "completedAt": "2026-08-28T09:30:00+00:00",
              "site": "https://ndstk.se/"
            }
            """;

        Assert.Null(ScanJson.Deserialize(json));
    }

    // The options that shape a scan are part of its record, because two scans run with different
    // options diff as though the site changed - a member scan against a public one differs by the
    // member cookie, which is an artefact of the run and not a change to the site.
    [Fact]
    public void The_options_summary_round_trips()
    {
        ScanResult sample = Sample() with
        {
            Options = new ScanOptionsSummary(MaxPages: 7, Locale: Locale.En, MemberScanEnabled: true, DryRun: false),
        };

        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(sample));

        Assert.NotNull(back?.Options);
        Assert.Equal(7, back.Options.MaxPages);
        Assert.Equal(Locale.En, back.Options.Locale);
        Assert.True(back.Options.MemberScanEnabled);
        Assert.False(back.Options.DryRun);
    }

    // Two shapes, and only the second is a history file written before this field existed. A null
    // Options has no [JsonIgnoreCondition], so this build writes it as an explicit "options": null -
    // the key is present, just empty - which is checked first below. A genuine pre-branch file has
    // no such key at all: it predates the field entirely, so nothing here ever serialized one. Both
    // shapes must still load, and both must say "not recorded" rather than claiming a default that
    // was never true.
    [Fact]
    public void A_result_without_an_options_summary_still_loads()
    {
        string json = ScanJson.Serialize(Sample() with { Options = null });

        Assert.Contains("\"options\": null", json);

        // Options is the last constructor parameter, so it is also the last property written: this
        // strips both its line and the now-trailing comma on the property before it, leaving exactly
        // what a file predating the field would look like - no "options" key at all.
        string preBranchJson = json.Replace(",\n  \"options\": null\n}", "\n}");

        ScanResult? back = ScanJson.Deserialize(preBranchJson);

        Assert.NotNull(back);
        Assert.Null(back.Options);
        Assert.Single(back.Candidates);
    }
}
