using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class BookableClassTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ClassKey = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Member = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private static TrainingClass Class(int capacity = 4, int hoursFromNow = 24) => new(
        Key: ClassKey,
        Title: "Nybörjartennis",
        Description: "Grunderna.",
        StartUtc: Now.AddHours(hoursFromNow),
        DurationMinutes: 60,
        Capacity: capacity,
        Instructor: "Anna",
        Location: "Bana 1");

    private static BookingSnapshot Booking(Guid member, string status = BookingStatus.Confirmed)
        => new(1, member, ClassKey, status, null, Now.AddHours(24), null);

    [Fact]
    public void An_empty_class_is_bookable_with_every_place_free()
    {
        BookableClass result = BookableClass.From(Class(), [], Member, Now);

        Assert.Equal(4, result.RemainingPlaces);
        Assert.True(result.CanBook);
        Assert.False(result.MemberHasBooking);
        Assert.False(result.IsFull);
    }

    [Fact]
    public void A_full_class_cannot_be_booked()
    {
        BookingSnapshot[] bookings = [Booking(Other), Booking(Other), Booking(Other), Booking(Other)];

        BookableClass result = BookableClass.From(Class(), bookings, Member, Now);

        Assert.Equal(0, result.RemainingPlaces);
        Assert.True(result.IsFull);
        Assert.False(result.CanBook);
    }

    // A member already holding a place must not be offered a second one, even with room left.
    [Fact]
    public void A_member_who_already_booked_cannot_book_again()
    {
        BookingSnapshot[] bookings = [Booking(Member)];

        BookableClass result = BookableClass.From(Class(), bookings, Member, Now);

        Assert.True(result.MemberHasBooking);
        Assert.False(result.CanBook);
        Assert.Equal(3, result.RemainingPlaces);
    }

    // A class that has already started is history, whatever room it has.
    [Fact]
    public void A_class_in_the_past_cannot_be_booked()
    {
        BookableClass result = BookableClass.From(Class(hoursFromNow: -1), [], Member, Now);

        Assert.False(result.CanBook);
        Assert.True(result.HasStarted);
    }

    [Fact]
    public void A_class_starting_right_now_counts_as_started()
    {
        BookableClass result = BookableClass.From(Class(hoursFromNow: 0), [], Member, Now);

        Assert.True(result.HasStarted);
        Assert.False(result.CanBook);
    }

    // An anonymous visitor sees availability but is never told they hold a booking.
    [Fact]
    public void With_no_member_the_class_reports_no_booking_of_its_own()
    {
        BookingSnapshot[] bookings = [Booking(Other)];

        BookableClass result = BookableClass.From(Class(), bookings, memberKey: null, Now);

        Assert.False(result.MemberHasBooking);
        Assert.Equal(3, result.RemainingPlaces);
        Assert.True(result.CanBook);
    }

    // A capacity an editor never filled in must not silently mean "unlimited".
    [Fact]
    public void A_class_with_no_capacity_is_not_bookable()
    {
        BookableClass result = BookableClass.From(Class(capacity: 0), [], Member, Now);

        Assert.Equal(0, result.RemainingPlaces);
        Assert.False(result.CanBook);
    }

    [Fact]
    public void Cancelled_bookings_free_their_place_up_again()
    {
        BookingSnapshot[] bookings =
        [
            Booking(Other, BookingStatus.Cancelled),
            Booking(Member, BookingStatus.Cancelled),
        ];

        BookableClass result = BookableClass.From(Class(), bookings, Member, Now);

        Assert.Equal(4, result.RemainingPlaces);
        Assert.False(result.MemberHasBooking);
        Assert.True(result.CanBook);
    }
}
