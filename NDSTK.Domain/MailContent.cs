namespace NDSTK.Booking.Domain;

/// <summary>A rendered mail, ready to hand to the sender.</summary>
public sealed record MailContent(string Subject, string HtmlBody);
