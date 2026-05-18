using Microsoft.AspNetCore.Identity;

namespace K_OCR.Identity;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    public string? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>When true, this user has elevated control over their organization.</summary>
    public bool IsOrganizationAdmin { get; set; }

    /// <summary>When true, the user retains super-admin privileges.</summary>
    public bool IsGlobalAdmin { get; set; }
}
