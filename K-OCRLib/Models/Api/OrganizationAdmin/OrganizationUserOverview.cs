namespace K_OCR.Models.Api.OrganizationAdmin;

public class OrganizationUserOverview
{
    public string UserId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
