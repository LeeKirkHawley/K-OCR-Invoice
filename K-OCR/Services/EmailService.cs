using System.Net;
using K_OCR.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace K_OCR.Services;

public class EmailService : IEmailService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfigurationService configService, ILogger<EmailService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task SendTestEmailAsync(string toEmail)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var message = BuildMessage(
            email,
            toEmail,
            displayName: null,
            subject: "K-OCR — SMTP test email",
            htmlBody: "<html><body style=\"font-family:sans-serif\"><p>This is a test email from K-OCR. If you received it, your SMTP settings are working.</p></body></html>");

        await SendAsync(email, message);
        _logger.LogInformation("Test email sent to {Email}.", toEmail);
    }

    public async Task SendOrgAdminInviteAsync(
        string toEmail,
        string toName,
        string organizationName,
        string setupLink)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = $"Your {organizationName} account on K-OCR is ready";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Welcome to K-OCR, {WebUtility.HtmlEncode(toName)}!</h2>
              <p>Your organization <strong>{WebUtility.HtmlEncode(organizationName)}</strong> has been set up.</p>
              <p>Click the button below to set your password and get started:</p>
              <p style="margin:24px 0">
                <a href="{setupLink}"
                   style="background:#1a6fc4;color:#fff;padding:12px 24px;border-radius:4px;text-decoration:none;font-weight:600">
                  Set My Password
                </a>
              </p>
              <p style="color:#666;font-size:0.9em">
                This link expires in 24 hours. If you did not expect this email, you can ignore it.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Invite email sent to {Email} for org '{Org}'.", toEmail, organizationName);
    }

    // -------------------------------------------------------------------------

    private static MimeMessage BuildMessage(
        EmailSettings email,
        string toAddress,
        string? displayName,
        string subject,
        string htmlBody)
    {
        // Fall back to the authenticated username when no real From address is configured.
        // Shared hosts (e.g. Network Solutions/FatCow) require the sender domain to resolve in DNS
        // and typically require it to match the authenticated account.
        var fromAddress = string.IsNullOrWhiteSpace(email.FromAddress)
            ? email.Username ?? "no-reply@example.com"
            : email.FromAddress;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromName ?? "K-OCR", fromAddress));
        message.To.Add(string.IsNullOrWhiteSpace(displayName)
            ? MailboxAddress.Parse(toAddress)
            : new MailboxAddress(displayName, toAddress));
        message.Subject = subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody };
        return message;
    }

    private async Task SendAsync(EmailSettings email, MimeMessage message)
    {
        // SecureSocketOptions.Auto: port 465 → SslOnConnect, port 587/25 → StartTls
        var socketOptions = email.EnableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;

        using var client = new SmtpClient();

        if (email.SkipCertificateValidation)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        await client.ConnectAsync(email.SmtpHost, email.Port, socketOptions);

        if (!string.IsNullOrWhiteSpace(email.Username))
            await client.AuthenticateAsync(email.Username, email.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
    }
}
