namespace K_OCRLib.Models;

public sealed class SuperAdminUserDetail
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public bool IsGlobalAdmin { get; set; }
}
