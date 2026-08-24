using NDSTK.Booking.Domain;

namespace NDSTK.Tests;

public class MailTemplateTests
{
    [Fact]
    public void Verification_mail_carries_the_link_and_a_swedish_subject()
    {
        MailContent mail = MailTemplates.Verification("https://ndstk.se/verifiera?member=abc&token=xyz");

        Assert.Contains("NDSTK", mail.Subject);
        Assert.Contains("verifiera?member=abc", mail.HtmlBody);
        Assert.Contains("Bekräfta", mail.Subject, StringComparison.OrdinalIgnoreCase);
    }

    // The token is base64-ish and arrives from ASP.NET Identity, and the whole URL is placed in an
    // href attribute. An unescaped quote would let the rest of the token become new HTML
    // attributes, so this is an injection guard, not cosmetics.
    [Fact]
    public void Verification_mail_escapes_the_link_into_the_attribute()
    {
        MailContent mail = MailTemplates.Verification("https://ndstk.se/v?t=\"><script>alert(1)</script>");

        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.DoesNotContain("\"><script", mail.HtmlBody);
    }

    [Fact]
    public void Verification_mail_ampersand_in_the_url_is_escaped()
    {
        MailContent mail = MailTemplates.Verification("https://ndstk.se/v?a=1&b=2");

        Assert.Contains("a=1&amp;b=2", mail.HtmlBody);
    }

    [Fact]
    public void Verification_mail_has_a_plain_text_fallback_of_the_url()
    {
        // Some mail clients strip links entirely; the raw URL has to be readable too.
        MailContent mail = MailTemplates.Verification("https://ndstk.se/verifiera?member=abc&token=xyz");

        Assert.Contains("https://ndstk.se/verifiera?member=abc&amp;token=xyz", mail.HtmlBody);
    }
}
