namespace K_OCR.Models.Api.SuperAdmin;

public class CreateOrganizationResult
{
    public string OrganizationId { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
}
