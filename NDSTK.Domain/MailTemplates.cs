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
    public static MailContent Verification(string verificationUrl)
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
                <p>Fungerar inte knappen? Kopiera den här adressen till din webbläsare:</p>
                <p style="word-break:break-all;font-size:13px;color:#666;">{url}</p>
                <p>Om du inte har registrerat dig hos oss kan du bortse från det här meddelandet.</p>
                """));
    }

    private const string ButtonStyle =
        "display:inline-block;padding:12px 20px;background:#001F54;color:#F7E300;"
        + "text-decoration:none;border-radius:4px;font-weight:600;";

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
