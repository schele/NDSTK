using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NDSTK.Booking.Domain;
using NDSTK.Booking.Security;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Services;

namespace NDSTK.Booking.Services;

/// <summary>
/// Sends the booking feature's mail through Umbraco's configured sender.
/// </summary>
/// <remarks>
/// Umbraco's EmailSender writes an .eml file when Smtp:PickupDirectoryLocation is set and sends
/// over SMTP otherwise, which is how the whole flow stays testable locally without the live
/// mailbox password. It does not, however, create that directory - it opens the file with
/// FileMode.CreateNew and throws if the folder is missing - and the folder sits under the
/// gitignored umbraco/Logs, so a fresh clone never has it. Hence EnsurePickupDirectory below.
/// </remarks>
public sealed class BookingMailService(
    IEmailSender emailSender,
    IMemberService memberService,
    IOptionsMonitor<GlobalSettings> globalSettings,
    IHostEnvironment hostEnvironment,
    ILogger<BookingMailService> logger)
{
    /// <summary>Groups these mails in Umbraco's send-email notification, if anything listens.</summary>
    private const string EmailType = "Membership";

    /// <summary>
    /// Sends the activation link, telling the member how long it lasts. The duration is read from
    /// the token provider's own options, so the sentence in the mail and the expiry Identity
    /// enforces are the same number by construction.
    /// </summary>
    public Task SendVerificationAsync(string toEmail, string verificationUrl)
        => SendAsync(toEmail, MailTemplates.Verification(
            verificationUrl, (int)MemberVerificationTokenOptions.Lifespan.TotalMinutes));

    /// <summary>
    /// Tells the owner of an already-active address that they have an account. Goes to the mailbox,
    /// never to the browser, so it does not reveal membership to whoever filled in the form.
    /// </summary>
    public Task SendAccountAlreadyExistsAsync(string toEmail, string loginUrl)
        => SendAsync(toEmail, MailTemplates.AccountAlreadyExists(loginUrl));

    /// <summary>
    /// Reminds a member about a class. Takes the member's key rather than an address because the
    /// reminder job works from booking rows, which carry the key and not the email.
    /// </summary>
    public async Task SendClassReminderAsync(
        Guid memberKey, string classTitle, DateTime startUtc, string? location, string? mapUrl)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member?.Email is not { Length: > 0 } email)
        {
            logger.LogWarning(
                "Cannot send a reminder to member {MemberKey}: no email address on record.", memberKey);
            return;
        }

        await SendAsync(email, MailTemplates.ClassReminder(classTitle, startUtc, location, null, mapUrl));
    }

    private async Task SendAsync(string toEmail, MailContent content)
    {
        SmtpSettings? smtp = globalSettings.CurrentValue.Smtp;
        if (smtp is null || emailSender.CanSendRequiredEmail() is false)
        {
            // Not thrown: a member who has registered should not see an error page because the
            // club's mail is misconfigured. They are told to check their inbox either way, and this
            // log line is what tells an administrator why nothing arrived.
            logger.LogError(
                "Cannot send '{Subject}' to {Email}: neither SMTP nor a pickup directory is configured.",
                content.Subject, toEmail);
            return;
        }

        EnsurePickupDirectory(smtp);

        var message = new EmailMessage(smtp.From, toEmail, content.Subject, content.HtmlBody, true);

        try
        {
            await emailSender.SendAsync(message, EmailType);
            logger.LogInformation("Sent '{Subject}' to {Email}.", content.Subject, toEmail);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "Failed to send '{Subject}' to {Email}.", content.Subject, toEmail);
        }
    }

    private void EnsurePickupDirectory(SmtpSettings smtp)
    {
        if (string.IsNullOrWhiteSpace(smtp.PickupDirectoryLocation))
        {
            return;
        }

        // The configured value is relative to the process working directory, which is how Umbraco
        // itself resolves it. Anchoring to the content root makes that explicit and survives a
        // process started from somewhere else.
        var path = Path.IsPathRooted(smtp.PickupDirectoryLocation)
            ? smtp.PickupDirectoryLocation
            : Path.Combine(hostEnvironment.ContentRootPath, smtp.PickupDirectoryLocation);

        Directory.CreateDirectory(path);
    }
}
