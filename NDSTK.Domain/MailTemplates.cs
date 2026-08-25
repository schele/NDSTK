using System.Net;

namespace NDSTK.Booking.Domain;

/// <summary>
/// The club's outgoing mail, as pure functions.
/// </summary>
/// <remarks>
/// Deliberately plain string building rather than Razor partials rendered to string. Rendering a
/// Razor view outside a request needs a synthetic ActionContext and HttpContext, and the reminder
/// job has no request at all - so the same mail would have to be produced two different ways.
/// These functions behave identically in a controller and in a background job, and being pure
/// makes the HTML escaping testable, which matters because a token goes into an href attribute.
/// </remarks>
public static class MailTemplates
{
    private const string ClubName = "NDSTK";

    /// <summary>Sent on registration. The link is the only way to activate the account.</summary>
    /// <param name="expiresInMinutes">
    /// How long the link stays valid. Passed in rather than hard-coded so the mail cannot promise a
    /// different number from the one the token provider actually enforces - a member told "24
    /// timmar" by a link that dies in fifteen minutes would reasonably conclude the site is broken.
    /// Stated in the mail at all because a link that has silently stopped working is the more
    /// confusing outcome: knowing it is short-lived is what prompts someone to use it now.
    /// </param>
    public static MailContent Verification(string verificationUrl, int expiresInMinutes)
    {
        // Escaped once, used twice: as the href and as the visible fallback text. Some mail clients
        // strip links, so the raw address has to be readable as well as clickable.
        var url = WebUtility.HtmlEncode(verificationUrl);

        return new MailContent(
            $"Bekräfta din e-postadress hos {ClubName}",
            Wrap($"""
                <p>Hej och välkommen till {ClubName}!</p>
                <p>Klicka på länken nedan för att bekräfta din e-postadress och aktivera ditt medlemskonto.</p>
                <p><a href="{url}" style="{ButtonStyle}">Bekräfta min e-postadress</a></p>
                <p><strong>Länken gäller i {expiresInMinutes} minuter.</strong> Har den slutat
                   fungera? Fyll i registreringen igen så skickar vi en ny.</p>
                <p>Fungerar inte knappen? Kopiera den här adressen till din webbläsare:</p>
                <p style="word-break:break-all;font-size:13px;color:#666;">{url}</p>
                <p>Om du inte har registrerat dig hos oss kan du bortse från det här meddelandet.</p>
                """));
    }

    /// <summary>
    /// Sent when somebody tries to register an address that already has an <em>active</em> account.
    /// </summary>
    /// <remarks>
    /// Safe despite the enumeration rules, because it goes to the mailbox rather than to the
    /// browser: the person filling in the form learns nothing, and only the address's actual owner
    /// ever sees it. Without it, someone who has simply forgotten they registered gets the same
    /// "check your inbox" message and then no mail at all, which looks like the site is broken.
    /// </remarks>
    public static MailContent AccountAlreadyExists(string loginUrl)
    {
        var url = WebUtility.HtmlEncode(loginUrl);

        return new MailContent(
            $"Du har redan ett konto hos {ClubName}",
            Wrap($"""
                <p>Hej! Någon försökte skapa ett konto med den här e-postadressen, men du har redan
                   ett.</p>
                <p>Logga in med din e-postadress och ditt lösenord i stället.</p>
                <p><a href="{url}" style="{ButtonStyle}">Logga in</a></p>
                <p style="word-break:break-all;font-size:13px;color:#666;">{url}</p>
                <p>Har du glömt lösenordet? Hör av dig till oss så hjälper vi dig.</p>
                <p>Var det inte du som försökte? Då behöver du inte göra något – ingen kommer åt
                   ditt konto utan ditt lösenord.</p>
                """));
    }

    /// <summary>
    /// Sent by the reminder job the configured number of hours before a class.
    /// </summary>
    /// <remarks>
    /// The time is rendered in Swedish local time, not UTC. A reminder that said "16:00" for a class
    /// that starts at 18:00 would send members to the courts two hours early - which is the whole
    /// reason the stored instant is UTC and the display is converted.
    /// </remarks>
    public static MailContent ClassReminder(
        string classTitle, DateTime startUtc, string? location, string? portalUrl, string? mapUrl = null)
    {
        var title = WebUtility.HtmlEncode(classTitle);
        DateTime local = SwedishTime.ToSwedish(startUtc);
        var when = local.ToString("dddd d MMMM 'kl.' HH:mm", Swedish);

        // The court, linked to the map when the club has an address configured. Worth more in a
        // reminder than anywhere else: this is the mail somebody opens on the way there.
        var place = string.IsNullOrWhiteSpace(location)
            ? string.Empty
            : string.IsNullOrWhiteSpace(mapUrl)
                ? WebUtility.HtmlEncode(location)
                : $"""<a href="{WebUtility.HtmlEncode(mapUrl)}">{WebUtility.HtmlEncode(location)}</a>""";

        var locationLine = place.Length == 0 ? string.Empty : $"<p>Plats: {place}</p>";

        var portalLine = string.IsNullOrWhiteSpace(portalUrl)
            ? string.Empty
            : $"""<p><a href="{WebUtility.HtmlEncode(portalUrl)}" style="{ButtonStyle}">Mina sidor</a></p>""";

        return new MailContent(
            $"Påminnelse: {classTitle} hos {ClubName} imorgon",
            Wrap($"""
                <p>Hej! Det här är en påminnelse om din träning.</p>
                <p><strong>{title}</strong><br />{when}</p>
                {locationLine}
                <p>Vi ses på banan!</p>
                {portalLine}
                """));
    }

    private static readonly System.Globalization.CultureInfo Swedish = new("sv-SE");

    /// <summary>
    /// Matches .btn-primary on the site: the club's yellow with navy text, not the other way round.
    /// Inline and hard-coded because mail clients discard stylesheets and none of them support
    /// custom properties, so the tokens in site.css cannot be reused here.
    /// </summary>
    private const string ButtonStyle =
        "display:inline-block;padding:12px 20px;background:#F7E300;color:#001F54;"
        + "text-decoration:none;border-radius:4px;font-weight:700;";

    /// <summary>
    /// The shared frame. Inline styles only - mail clients discard stylesheets, and roughly none of
    /// them support custom properties, so the brand colours are repeated as literals here rather
    /// than shared with site.css.
    /// </summary>
    private static string Wrap(string body) => $"""
        <div style="font-family:'Segoe UI',Roboto,sans-serif;color:#222;line-height:1.5;max-width:560px;">
          <h2 style="color:#001F54;margin:0 0 16px;">{ClubName}</h2>
          {body}
          <hr style="border:0;border-top:1px solid #E5E6E8;margin:24px 0;" />
          <p style="font-size:12px;color:#666;">
            {ClubName} — Norra Djurgårdsstadens Tennisklubb
          </p>
        </div>
        """;
}
