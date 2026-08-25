using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class BookableClassTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ClassKey = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly Guid Elsa = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Nils = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>A solo account: one child.</summary>
    private static readonly Guid[] Mine = [Elsa];

    /// <summary>A family account: two children on one login.</summary>
    private static readonly Guid[] MyFamily = [Elsa, Nils];

    /// <summary>An anonymous visitor has no children at all.</summary>
    private static readonly Guid[] Anonymous = [];

    private static TrainingClass Class(int capacity = 4, int hoursFromNow = 24) => new(
        Key: ClassKey,
        Title: "Nybörjartennis",
        Description: "Grunderna.",
        StartUtc: Now.AddHours(hoursFromNow),
        DurationMinutes: 60,
        Capacity: capacity,
        Instructor: new ClassInstructor("Anna"),
        Location: "Bana 1");

    private static BookingSnapshot Booking(Guid participant, string status = BookingStatus.Confirmed)
        => new(1, Guid.NewGuid(), participant, ClassKey, status, null, Now.AddHours(24), null);

    [Fact]
    public void An_empty_class_is_bookable_with_every_place_free()
    {
        BookableClass result = BookableClass.From(Class(), [], Mine, Now);

        Assert.Equal(4, result.RemainingPlaces);
        Assert.True(result.CanBook);
        Assert.False(result.MemberHasBooking);
        Assert.False(result.IsFull);
    }

    [Fact]
    public void A_full_class_cannot_be_booked()
    {
        BookingSnapshot[] bookings =
            [Booking(Stranger), Booking(Stranger), Booking(Stranger), Booking(Stranger)];

        BookableClass result = BookableClass.From(Class(), bookings, Mine, Now);

        Assert.Equal(0, result.RemainingPlaces);
        Assert.True(result.IsFull);
        Assert.False(result.CanBook);
    }

    // A child already holding a place must not be offered a second one, even with room left.
    [Fact]
    public void A_child_who_already_booked_cannot_book_again()
    {
        BookingSnapshot[] bookings = [Booking(Elsa)];

        BookableClass result = BookableClass.From(Class(), bookings, Mine, Now);

        Assert.True(result.MemberHasBooking);
        Assert.False(result.CanBook);
        Assert.Equal(3, result.RemainingPlaces);
    }

    // The family case the old per-account flag got wrong: one child booked, the other still free.
    [Fact]
    public void A_family_may_still_book_a_second_child_onto_a_class_the_first_is_on()
    {
        BookingSnapshot[] bookings = [Booking(Elsa)];

        BookableClass result = BookableClass.From(Class(), bookings, MyFamily, Now);

        Assert.True(result.MemberHasBooking);
        Assert.True(result.CanBook);
        Assert.Equal([Elsa], result.BookedParticipantKeys);
        Assert.Equal([Nils], result.BookableParticipantKeys);
    }

    [Fact]
    public void A_family_with_every_child_booked_cannot_book_again()
    {
        BookingSnapshot[] bookings = [Booking(Elsa), Booking(Nils)];

        BookableClass result = BookableClass.From(Class(), bookings, MyFamily, Now);

        Assert.False(result.CanBook);
        Assert.Empty(result.BookableParticipantKeys);
    }

    // A class that has already started is history, whatever room it has.
    [Fact]
    public void A_class_in_the_past_cannot_be_booked()
    {
        BookableClass result = BookableClass.From(Class(hoursFromNow: -1), [], Mine, Now);

        Assert.False(result.CanBook);
        Assert.True(result.HasStarted);
    }

    [Fact]
    public void A_class_starting_right_now_counts_as_started()
    {
        BookableClass result = BookableClass.From(Class(hoursFromNow: 0), [], Mine, Now);

        Assert.True(result.HasStarted);
        Assert.False(result.CanBook);
    }

    // An anonymous visitor sees availability but is never told they hold a booking. The button
    // stays visible so they are prompted to sign in rather than shown a class that looks closed.
    [Fact]
    public void With_no_member_the_class_reports_no_booking_of_its_own()
    {
        BookingSnapshot[] bookings = [Booking(Stranger)];

        BookableClass result = BookableClass.From(Class(), bookings, Anonymous, Now);

        Assert.False(result.MemberHasBooking);
        Assert.Equal(3, result.RemainingPlaces);
        Assert.True(result.CanBook);
    }

    // A capacity an editor never filled in must not silently mean "unlimited".
    [Fact]
    public void A_class_with_no_capacity_is_not_bookable()
    {
        BookableClass result = BookableClass.From(Class(capacity: 0), [], Mine, Now);

        Assert.Equal(0, result.RemainingPlaces);
        Assert.False(result.CanBook);
    }

    [Fact]
    public void Cancelled_bookings_free_their_place_up_again()
    {
        BookingSnapshot[] bookings =
        [
            Booking(Stranger, BookingStatus.Cancelled),
            Booking(Elsa, BookingStatus.Cancelled),
        ];

        BookableClass result = BookableClass.From(Class(), bookings, Mine, Now);

        Assert.Equal(4, result.RemainingPlaces);
        Assert.False(result.MemberHasBooking);
        Assert.True(result.CanBook);
    }

    // A class the whole account is already on has nothing left to offer it, and drops out of
    // "Boka träning" - it is in "Mina bokningar" instead. Left there, stripped of its buttons and
    // carrying only a "Bokad:" label, it reads as something you failed to do rather than something
    // you have already done.
    [Fact]
    public void A_solo_account_whose_child_is_booked_has_nothing_left_on_the_class()
    {
        BookingSnapshot[] bookings = [Booking(Elsa)];

        BookableClass result = BookableClass.From(Class(), bookings, Mine, Now);

        Assert.True(result.EveryChildBooked);
    }

    // The case that must NOT drop out: one sibling booked, the other still free to book.
    [Fact]
    public void A_family_with_one_child_still_to_book_keeps_the_class()
    {
        BookingSnapshot[] bookings = [Booking(Elsa)];

        BookableClass result = BookableClass.From(Class(), bookings, MyFamily, Now);

        Assert.False(result.EveryChildBooked);
    }

    [Fact]
    public void A_family_with_both_children_booked_has_nothing_left_on_the_class()
    {
        BookingSnapshot[] bookings = [Booking(Elsa), Booking(Nils)];

        BookableClass result = BookableClass.From(Class(), bookings, MyFamily, Now);

        Assert.True(result.EveryChildBooked);
    }

    // An anonymous visitor has no children, so nothing of theirs is booked and the list stays a
    // full shop window rather than collapsing to nothing.
    [Fact]
    public void An_anonymous_visitor_never_has_everything_booked()
    {
        BookingSnapshot[] bookings = [Booking(Stranger), Booking(Elsa)];

        BookableClass result = BookableClass.From(Class(), bookings, Anonymous, Now);

        Assert.False(result.EveryChildBooked);
    }

    // Cancelling frees the child up again, so the class comes back onto the list.
    [Fact]
    public void Cancelling_puts_the_class_back_on_offer()
    {
        BookingSnapshot[] bookings = [Booking(Elsa, BookingStatus.Cancelled)];

        BookableClass result = BookableClass.From(Class(), bookings, Mine, Now);

        Assert.False(result.EveryChildBooked);
    }
}
