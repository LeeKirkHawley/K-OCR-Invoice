namespace K_OCR.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends an org-admin invitation email with a password-setup link.
    /// </summary>
    Task SendOrgAdminInviteAsync(
        string toEmail,
        string toName,
        string organizationName,
        string setupLink);

    /// <summary>
    /// Sends a plain test email to verify SMTP configuration.
    /// </summary>
    Task SendTestEmailAsync(string toEmail);
}
