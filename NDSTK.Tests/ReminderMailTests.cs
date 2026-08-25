using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class ReminderMailTests
{
    private static readonly DateTime StartUtc = new(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reminder_names_the_class_and_the_swedish_local_time()
    {
        // 16:00 UTC in July is 18:00 in Sweden. A reminder that said 16:00 would send members to
        // the courts two hours early.
        MailContent mail = MailTemplates.ClassReminder("Teknikpass", StartUtc, "Bana 1", null);

        Assert.Contains("Teknikpass", mail.HtmlBody);
        Assert.Contains("18:00", mail.HtmlBody);
        Assert.DoesNotContain("16:00", mail.HtmlBody);
        Assert.Contains("Bana 1", mail.HtmlBody);
    }

    // WebUtility.HtmlEncode escapes everything above ASCII into numeric entities, so Swedish
    // characters arrive as &#246; rather than ö. Harmless - every mail client renders them - and
    // worth a battle-tested encoder rather than a hand-rolled five-character one that only looks
    // right until it meets an attribute context.
    [Fact]
    public void Reminder_entity_encodes_swedish_characters_in_the_body()
    {
        MailContent mail = MailTemplates.ClassReminder("Nybörjartennis", StartUtc, null, null);

        Assert.Contains("Nyb&#246;rjartennis", mail.HtmlBody);
    }

    // The subject is not HTML, so it keeps the real characters.
    [Fact]
    public void Reminder_subject_keeps_swedish_characters_unencoded()
    {
        MailContent mail = MailTemplates.ClassReminder("Nybörjartennis", StartUtc, null, null);

        Assert.Contains("Nybörjartennis", mail.Subject);
        Assert.DoesNotContain("&#246;", mail.Subject);
    }

    [Fact]
    public void Reminder_subject_mentions_the_club_and_the_class()
    {
        MailContent mail = MailTemplates.ClassReminder("Teknikpass", StartUtc, null, null);

        Assert.Contains("NDSTK", mail.Subject);
        Assert.Contains("Teknikpass", mail.Subject);
    }

    // Class titles are editor input, and they land in HTML.
    [Fact]
    public void Reminder_escapes_the_class_title_and_location()
    {
        MailContent mail = MailTemplates.ClassReminder(
            "<script>alert(1)</script>", StartUtc, "<b>Bana</b>", null);

        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.DoesNotContain("<b>Bana</b>", mail.HtmlBody);
    }

    [Fact]
    public void Reminder_omits_the_location_line_when_there_is_none()
    {
        MailContent mail = MailTemplates.ClassReminder("Teknikpass", StartUtc, null, null);

        Assert.DoesNotContain("Plats", mail.HtmlBody);
    }

    [Fact]
    public void Reminder_links_to_the_portal_when_given_one()
    {
        MailContent mail = MailTemplates.ClassReminder(
            "Teknikpass", StartUtc, null, "https://ndstk.se/mina-sidor/");

        Assert.Contains("https://ndstk.se/mina-sidor/", mail.HtmlBody);
    }

    // The reminder is the mail somebody opens on the way to the courts, so the court name links to
    // the map when the club has an address configured.
    [Fact]
    public void Reminder_links_the_location_to_the_map()
    {
        var map = MapLink.ForAddress("Lidingövägen 1, Stockholm");

        MailContent mail = MailTemplates.ClassReminder("Teknikpass", StartUtc, "Bana 1", null, map);

        // The ampersand before query= arrives as &amp;, which is what an href in an HTML attribute
        // has to carry - a bare & there is a malformed entity, and mail clients are the last place
        // to find out whether one is forgiving.
        Assert.Contains(
            """<a href="https://www.google.com/maps/search/?api=1&amp;query=""", mail.HtmlBody);
        Assert.Contains(">Bana 1</a>", mail.HtmlBody);
    }

    // No address on Inställningar: the court is still named, just not linked. An <a> with an empty
    // href would look like a link and go nowhere.
    [Fact]
    public void Without_an_address_the_location_stays_plain_text()
    {
        MailContent mail = MailTemplates.ClassReminder("Teknikpass", StartUtc, "Bana 1", null, null);

        Assert.Contains("Plats: Bana 1", mail.HtmlBody);
        Assert.DoesNotContain("<a href=\"\"", mail.HtmlBody);
    }

    // A class with no location has no line at all, whether or not an address is configured - a
    // "Plats:" with nothing after it is worse than silence.
    [Fact]
    public void An_address_alone_does_not_produce_a_location_line()
    {
        MailContent mail = MailTemplates.ClassReminder(
            "Teknikpass", StartUtc, null, null, MapLink.ForAddress("Lidingövägen 1, Stockholm"));

        Assert.DoesNotContain("Plats:", mail.HtmlBody);
    }
}
