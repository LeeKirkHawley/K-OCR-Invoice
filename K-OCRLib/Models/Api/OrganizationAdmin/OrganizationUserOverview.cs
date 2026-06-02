namespace K_OCRLib.Models.Api.OrganizationAdmin;

public class OrganizationUserOverview
{
    public string UserId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public bool IsOrganizationAdmin { get; set; }
    public bool CanChangeRole { get; set; }
    public bool CanRemove { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
