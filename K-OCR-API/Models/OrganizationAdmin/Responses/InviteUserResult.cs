namespace K_OCR_API.Models.OrganizationAdmin.Responses;

public class InviteUserResult
{
    public string UserId { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
}
