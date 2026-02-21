namespace K_OCR_API.Models.OrganizationAdmin.Responses;

public class OrganizationUserOverview
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
