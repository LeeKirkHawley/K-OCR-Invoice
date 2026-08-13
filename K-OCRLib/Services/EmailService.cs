using System.Net;
using K_OCRLib.Configuration;
using K_OCRLib.Services.Interfaces;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace K_OCRLib.Services;

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

    public async Task SendPasswordResetAsync(string toEmail, string toName, string resetLink)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = "K-OCR — Reset your password";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Reset your K-OCR password</h2>
              <p>Hi {WebUtility.HtmlEncode(toName)},</p>
              <p>We received a request to reset the password for your K-OCR account.</p>
              <p>Click the button below to set a new password:</p>
              <p style="margin:24px 0">
                <a href="{resetLink}"
                   style="background:#1a6fc4;color:#fff;padding:12px 24px;border-radius:4px;text-decoration:none;font-weight:600">
                  Reset My Password
                </a>
              </p>
              <p style="color:#666;font-size:0.9em">
                This link expires in 24 hours. If you did not request a password reset, you can ignore this email.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Password reset email sent to {Email}.", toEmail);
    }

    public async Task SendOrgUserInviteAsync(
        string toEmail,
        string toName,
        string organizationName,
        string role,
        string setupLink)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var friendlyRole = role switch
        {
            "OrganizationAdmin"     => "Administrator",
            "OrganizationValidator" => "Validator",
            _                       => "User"
        };

        var subject = $"You've been invited to {organizationName} on K-OCR";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Welcome to K-OCR, {WebUtility.HtmlEncode(toName)}!</h2>
              <p>You have been invited to <strong>{WebUtility.HtmlEncode(organizationName)}</strong> as a <strong>{WebUtility.HtmlEncode(friendlyRole)}</strong>.</p>
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
        _logger.LogInformation("Invite email sent to {Email} for org '{Org}' with role '{Role}'.", toEmail, organizationName, role);
    }

    public async Task SendOrgDeletionNotificationAsync(
        string toEmail,
        string toName,
        string organizationName)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = $"K-OCR — {WebUtility.HtmlEncode(organizationName)} marked for deletion";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Organization Marked for Deletion</h2>
              <p>Hi {WebUtility.HtmlEncode(toName)},</p>
              <p>Your organization <strong>{WebUtility.HtmlEncode(organizationName)}</strong> has been marked for deletion on K-OCR.</p>
              <p>If you would like to reactivate this organization, please contact Elk Mountain Software at <strong>leekirkhawley@gmail.com</strong>.</p>
              <p style="color:#666;font-size:0.9em;margin-top:24px">
                This organization will be permanently deleted after the configured retention period expires.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Organization deletion notification sent to {Email} for org '{Org}'.", toEmail, organizationName);
    }

    public async Task SendBatchSoftDeletedNotificationAsync(
        string toEmail,
        string toName,
        string organizationName,
        string batchName,
        int retentionDays)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = $"K-OCR — Batch '{WebUtility.HtmlEncode(batchName)}' marked for deletion";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Batch Marked for Deletion</h2>
              <p>Hi {WebUtility.HtmlEncode(toName)},</p>
              <p>The batch <strong>{WebUtility.HtmlEncode(batchName)}</strong> in your organization <strong>{WebUtility.HtmlEncode(organizationName)}</strong> has been marked for deletion.</p>
              <p>The batch and all its associated files will be permanently deleted in <strong>{retentionDays} day(s)</strong>.</p>
              <p style="color:#666;font-size:0.9em;margin-top:24px">
                If this was a mistake, please contact your system administrator before the retention period expires.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Batch soft-delete notification sent to {Email} for batch '{Batch}' in org '{Org}'.", toEmail, batchName, organizationName);
    }

    public async Task SendBatchHardDeletedNotificationAsync(
        string toEmail,
        string toName,
        string organizationName,
        string batchName)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = $"K-OCR — Batch '{WebUtility.HtmlEncode(batchName)}' permanently deleted";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Batch Permanently Deleted</h2>
              <p>Hi {WebUtility.HtmlEncode(toName)},</p>
              <p>The batch <strong>{WebUtility.HtmlEncode(batchName)}</strong> in your organization <strong>{WebUtility.HtmlEncode(organizationName)}</strong> has been permanently deleted, including all associated files and data.</p>
              <p style="color:#666;font-size:0.9em;margin-top:24px">
                If you did not expect this, please contact your system administrator.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Batch hard-delete notification sent to {Email} for batch '{Batch}' in org '{Org}'.", toEmail, batchName, organizationName);
    }

    public async Task SendBatchRestoredNotificationAsync(
        string toEmail,
        string toName,
        string organizationName,
        string batchName)
    {
        var settings = await _configService.LoadSettingsAsync();
        var email = settings.Email ?? new EmailSettings();

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        var subject = $"K-OCR — Batch '{WebUtility.HtmlEncode(batchName)}' restored";
        var body = $"""
            <html><body style="font-family:sans-serif;color:#222">
              <h2>Batch Restored</h2>
              <p>Hi {WebUtility.HtmlEncode(toName)},</p>
              <p>The batch <strong>{WebUtility.HtmlEncode(batchName)}</strong> in your organization <strong>{WebUtility.HtmlEncode(organizationName)}</strong> has been restored and is now active again.</p>
              <p style="color:#666;font-size:0.9em;margin-top:24px">
                You can now continue working with this batch.
              </p>
            </body></html>
            """;

        var message = BuildMessage(email, toEmail, toName, subject, body);
        await SendAsync(email, message);
        _logger.LogInformation("Batch restore notification sent to {Email} for batch '{Batch}' in org '{Org}'.", toEmail, batchName, organizationName);
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

        using var protocolLog = new MemoryStream();
        using var client = new SmtpClient(new ProtocolLogger(protocolLog));

        if (email.SkipCertificateValidation)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        try
        {
            await client.ConnectAsync(email.SmtpHost, email.Port, socketOptions);

            if (!string.IsNullOrWhiteSpace(email.Username))
                await client.AuthenticateAsync(email.Username, email.Password ?? string.Empty);

            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        finally
        {
            var log = System.Text.Encoding.UTF8.GetString(protocolLog.ToArray());
            if (!string.IsNullOrWhiteSpace(log))
                _logger.LogDebug("SMTP protocol log:\n{SmtpLog}", log);
        }
    }
}
