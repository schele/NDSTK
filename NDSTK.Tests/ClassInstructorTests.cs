using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

/// <summary>
/// HasDetails is the switch the class listing renders from: false leaves the coach's name as plain
/// text, true turns it into a button that opens their profile. Getting it wrong either way is
/// visible on the page - a button that opens an empty box, or a filled-in profile no visitor can
/// reach - so the rule is pinned here rather than left to the view.
/// </summary>
public class ClassInstructorTests
{
    // What the import that turned the old text field into nodes produces: a name and nothing else.
    [Fact]
    public void A_coach_with_only_a_name_has_nothing_to_show()
    {
        var coach = new ClassInstructor("Anna Lind");

        Assert.False(coach.HasDetails);
    }

    [Fact]
    public void A_title_is_enough_to_show_a_profile()
    {
        var coach = new ClassInstructor("Anna Lind", Title: "Huvudtränare");

        Assert.True(coach.HasDetails);
    }

    [Fact]
    public void A_quote_is_enough_to_show_a_profile()
    {
        var coach = new ClassInstructor("Anna Lind", Quote: "Tennis är rytm.");

        Assert.True(coach.HasDetails);
    }

    [Fact]
    public void Merits_are_enough_to_show_a_profile()
    {
        var coach = new ClassInstructor("Anna Lind", Merits: "<ul><li>SvTF steg 3</li></ul>");

        Assert.True(coach.HasDetails);
    }

    [Fact]
    public void A_photo_is_enough_to_show_a_profile()
    {
        var coach = new ClassInstructor("Anna Lind", PhotoUrl: "/media/anna.jpg");

        Assert.True(coach.HasDetails);
    }

    // An editor who clears a field leaves an empty string behind rather than a null, and Umbraco
    // hands back "" for a property that was never filled in. Both have to count as absent, or the
    // button appears on a profile with nothing in it.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_fields_count_as_absent(string blank)
    {
        var coach = new ClassInstructor("Anna Lind", blank, blank, blank, blank);

        Assert.False(coach.HasDetails);
    }
}
