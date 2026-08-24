using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CapacityTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ClassStart = new(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Member = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static BookingSnapshot Booking(string status, DateTime? holdExpires = null, Guid? member = null)
        => new(1, member ?? Guid.NewGuid(), Guid.NewGuid(), status, holdExpires, ClassStart, null);

    [Fact]
    public void An_empty_class_has_every_place_free()
        => Assert.Equal(8, Capacity.RemainingPlaces(8, [], Now));

    [Fact]
    public void Confirmed_bookings_take_places()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
        ];

        Assert.Equal(6, Capacity.RemainingPlaces(8, bookings, Now));
    }

    // An unpaid booking still holds the place while the member is on the Swish page.
    [Fact]
    public void An_unexpired_hold_takes_a_place()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Pending, Now.AddMinutes(5))];

        Assert.Equal(7, Capacity.RemainingPlaces(8, bookings, Now));
    }

    // ...but an abandoned one must not, or classes silently fill with ghosts.
    [Fact]
    public void An_expired_hold_releases_its_place()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Pending, Now.AddMinutes(-1))];

        Assert.Equal(8, Capacity.RemainingPlaces(8, bookings, Now));
    }

    [Fact]
    public void Cancelled_and_expired_bookings_do_not_take_places()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Cancelled),
            Booking(BookingStatus.Expired),
        ];

        Assert.Equal(8, Capacity.RemainingPlaces(8, bookings, Now));
    }

    [Fact]
    public void A_full_class_has_no_places_left()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed), Booking(BookingStatus.Confirmed)];

        Assert.Equal(0, Capacity.RemainingPlaces(2, bookings, Now));
    }

    // An editor may lower capacity below the places already taken. Existing bookings stand and
    // the count must not go negative, or the portal would render "-3 platser kvar".
    [Fact]
    public void Reducing_capacity_below_the_places_taken_never_goes_negative()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
            Booking(BookingStatus.Confirmed),
        ];

        Assert.Equal(0, Capacity.RemainingPlaces(1, bookings, Now));
    }

    [Fact]
    public void A_member_with_a_confirmed_booking_has_a_live_booking()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed, member: Member)];

        Assert.True(Capacity.HasLiveBooking(bookings, Member, Now));
    }

    [Fact]
    public void A_member_whose_only_booking_was_cancelled_may_book_again()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Cancelled, member: Member)];

        Assert.False(Capacity.HasLiveBooking(bookings, Member, Now));
    }
}
