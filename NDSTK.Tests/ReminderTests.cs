using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class ReminderTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static BookingSnapshot Booking(
        int id, DateTime classStartUtc, string status = BookingStatus.Confirmed,
        DateTime? reminderSentUtc = null, DateTime? holdExpiresUtc = null)
        => new(id, Guid.NewGuid(), Guid.NewGuid(), status, holdExpiresUtc, classStartUtc, reminderSentUtc);

    [Fact]
    public void A_class_inside_the_window_is_due()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(20))];

        Assert.Equal([1], Reminders.Due(bookings, Now, 24).Select(b => b.Id));
    }

    [Fact]
    public void A_class_beyond_the_window_is_not_yet_due()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(25))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    [Fact]
    public void A_class_that_has_already_started_is_not_reminded()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddMinutes(-1))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    // Idempotence: the job runs every 15 minutes, so a stamped booking must never resend.
    [Fact]
    public void An_already_reminded_booking_is_not_reminded_again()
    {
        BookingSnapshot[] bookings = [Booking(1, Now.AddHours(20), reminderSentUtc: Now.AddHours(-1))];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    // An unpaid hold is not a booking, so it gets no reminder.
    [Fact]
    public void A_pending_or_cancelled_booking_is_not_reminded()
    {
        BookingSnapshot[] bookings =
        [
            Booking(1, Now.AddHours(20), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(5)),
            Booking(2, Now.AddHours(20), BookingStatus.Cancelled),
        ];

        Assert.Empty(Reminders.Due(bookings, Now, 24));
    }

    [Fact]
    public void Due_reminders_come_back_soonest_first()
    {
        BookingSnapshot[] bookings =
        [
            Booking(1, Now.AddHours(20)),
            Booking(2, Now.AddHours(2)),
            Booking(3, Now.AddHours(10)),
        ];

        Assert.Equal([2, 3, 1], Reminders.Due(bookings, Now, 24).Select(b => b.Id));
    }

    [Fact]
    public void Only_pending_bookings_whose_hold_ran_out_are_swept()
    {
        BookingSnapshot[] bookings =
        [
            Booking(1, Now.AddDays(3), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(-1)),
            Booking(2, Now.AddDays(3), BookingStatus.Pending, holdExpiresUtc: Now.AddMinutes(5)),
            Booking(3, Now.AddDays(3), BookingStatus.Confirmed),
        ];

        Assert.Equal([1], Reminders.ExpiredHolds(bookings, Now).Select(b => b.Id));
    }
}
