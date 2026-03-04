namespace K_OCR.Models.Api.SuperAdmin;

public class CreateOrganizationResult
{
    public string OrganizationId { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    /// <summary>Full setup link sent in the invite email (null when email is not bypassed).</summary>
    public string? SetupLink { get; set; }
    public bool EmailSent { get; set; }
}
