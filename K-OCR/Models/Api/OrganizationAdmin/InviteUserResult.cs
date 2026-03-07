namespace K_OCR.Models.Api.OrganizationAdmin;

public class InviteUserResult
{
    public string UserId { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;

    /// <summary>Password-setup link for manual sharing when SMTP is not configured.</summary>
    public string? SetupLink { get; set; }

    /// <summary>True when the invitation email was successfully dispatched.</summary>
    public bool EmailSent { get; set; }
}
