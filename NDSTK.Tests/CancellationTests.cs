using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CancellationTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private const int Deadline = 12;

    [Fact]
    public void A_class_well_in_the_future_can_be_cancelled()
        => Assert.True(Cancellation.IsOpen(Now.AddDays(3), Now, Deadline));

    [Fact]
    public void A_class_just_outside_the_deadline_can_still_be_cancelled()
        => Assert.True(Cancellation.IsOpen(Now.AddHours(12).AddMinutes(1), Now, Deadline));

    // The boundary has to fall one way. Exactly on the deadline counts as closed, because closing
    // early is the direction that matches the point of having a deadline.
    [Fact]
    public void A_class_exactly_on_the_deadline_is_already_closed()
        => Assert.False(Cancellation.IsOpen(Now.AddHours(12), Now, Deadline));

    [Fact]
    public void A_class_inside_the_deadline_cannot_be_cancelled()
        => Assert.False(Cancellation.IsOpen(Now.AddHours(11), Now, Deadline));

    [Fact]
    public void A_class_about_to_start_cannot_be_cancelled()
        => Assert.False(Cancellation.IsOpen(Now.AddMinutes(5), Now, Deadline));

    // A class that has already run was never cancellable, and the deadline does not change that.
    [Fact]
    public void A_class_in_the_past_cannot_be_cancelled()
        => Assert.False(Cancellation.IsOpen(Now.AddHours(-1), Now, Deadline));

    // A zero deadline restores the old behaviour - cancellable right up to the start - rather than
    // rejecting everything, which is what a club would expect from clearing the field.
    [Fact]
    public void A_zero_deadline_allows_cancelling_right_up_to_the_start()
    {
        Assert.True(Cancellation.IsOpen(Now.AddMinutes(1), Now, 0));
        Assert.False(Cancellation.IsOpen(Now, Now, 0));
    }

    [Fact]
    public void The_earliest_cancellable_start_is_the_deadline_ahead_of_now()
        => Assert.Equal(Now.AddHours(12), Cancellation.EarliestCancellableStart(Now, Deadline));

    // The two halves of the rule have to agree: anything at or before the cutoff is closed,
    // anything after it is open. The SQL uses the cutoff, the view uses IsOpen.
    [Fact]
    public void The_cutoff_and_the_predicate_describe_the_same_boundary()
    {
        DateTime cutoff = Cancellation.EarliestCancellableStart(Now, Deadline);

        Assert.False(Cancellation.IsOpen(cutoff, Now, Deadline));
        Assert.True(Cancellation.IsOpen(cutoff.AddTicks(1), Now, Deadline));
    }
}
