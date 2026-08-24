using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class CapacityTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ClassStart = new(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Elsa = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Nils = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Vera = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>One guardian, so the sibling tests below are genuinely one family account.</summary>
    private static readonly Guid Guardian = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private static readonly Guid TheClass = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static BookingSnapshot Booking(
        string status, DateTime? holdExpires = null, Guid? participant = null)
        => new(1, Guardian, participant ?? Guid.NewGuid(), TheClass, status, holdExpires, ClassStart, null);

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
    public void A_child_with_a_confirmed_booking_has_a_live_booking()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed, participant: Elsa)];

        Assert.True(Capacity.HasLiveBooking(bookings, Elsa, Now));
    }

    [Fact]
    public void A_child_whose_only_booking_was_cancelled_may_book_again()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Cancelled, participant: Elsa)];

        Assert.False(Capacity.HasLiveBooking(bookings, Elsa, Now));
    }

    // The whole point of a family account: under the old rule, keyed on the account, the second of
    // these was rejected as a duplicate and a parent could not put two children in one group.
    [Fact]
    public void Two_siblings_may_both_hold_a_live_booking_on_the_same_class()
    {
        BookingSnapshot[] bookings =
        [
            Booking(BookingStatus.Confirmed, participant: Elsa),
            Booking(BookingStatus.Confirmed, participant: Nils),
        ];

        Assert.True(Capacity.HasLiveBooking(bookings, Elsa, Now));
        Assert.True(Capacity.HasLiveBooking(bookings, Nils, Now));
        Assert.Equal(6, Capacity.RemainingPlaces(8, bookings, Now));
    }

    [Fact]
    public void A_sibling_booking_does_not_make_another_child_look_booked()
    {
        BookingSnapshot[] bookings = [Booking(BookingStatus.Confirmed, participant: Elsa)];

        Assert.False(Capacity.HasLiveBooking(bookings, Vera, Now));
    }
}
