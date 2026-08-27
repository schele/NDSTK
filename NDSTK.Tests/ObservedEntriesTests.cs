using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class ObservedEntriesTests
{
    private static ObservedEntry Entry(string name, ConsentPass pass, StorageKind storage = StorageKind.Cookie)
        => new(name, storage, pass, $"https://ndstk.se/{pass}", null);

    // The same cookie appears in every pass from the one that set it onwards. Only the first
    // appearance carries information about which category it belongs to.
    [Fact]
    public void The_earliest_pass_wins_regardless_of_input_order()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("_ga_ABC", ConsentPass.AcceptAll),
            Entry("_ga_ABC", ConsentPass.Statistics),
            Entry("_ga_ABC", ConsentPass.Marketing),
        ]);

        Assert.Single(reduced);
        Assert.Equal(ConsentPass.Statistics, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void The_url_of_the_earliest_appearance_is_kept()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("cookie", ConsentPass.AcceptAll),
            Entry("cookie", ConsentPass.Undecided),
        ]);

        Assert.Equal("https://ndstk.se/Undecided", reduced[0].FirstSeenUrl);
    }

    // A localStorage key and a cookie can legitimately share a name, and they are different
    // declarations with different durations. Collapsing them would lose one.
    [Fact]
    public void The_same_name_in_two_storage_kinds_stays_two_entries()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("theme", ConsentPass.Preferences, StorageKind.Cookie),
            Entry("theme", ConsentPass.Preferences, StorageKind.LocalStorage),
        ]);

        Assert.Equal(2, reduced.Count);
    }

    // The member dimension runs last and visits different URLs. A cookie seen in both must be
    // attributed to the public pass that saw it first, not to the member area.
    [Fact]
    public void A_public_pass_beats_the_member_dimension()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("cookie", ConsentPass.MemberArea),
            Entry("cookie", ConsentPass.RejectAll),
        ]);

        Assert.Equal(ConsentPass.RejectAll, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void Matching_names_ignores_case()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("UMB_MEMBER", ConsentPass.AcceptAll),
            Entry("umb_member", ConsentPass.Undecided),
        ]);

        Assert.Single(reduced);
        Assert.Equal(ConsentPass.Undecided, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void An_empty_input_is_an_empty_result()
    {
        Assert.Empty(ObservedEntries.EarliestPerName([]));
    }
}
