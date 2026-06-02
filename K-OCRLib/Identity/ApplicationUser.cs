using Microsoft.AspNetCore.Identity;

namespace K_OCRLib.Identity;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    /// <summary>
    /// Preferred active organization for this user session context.
    /// Must also exist in <see cref="OrganizationMemberships"/> unless the user is a global admin.
    /// </summary>
    public string? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public ICollection<UserOrganizationMembership> OrganizationMemberships { get; set; } =
        new List<UserOrganizationMembership>();

    /// <summary>When true, the user retains super-admin privileges.</summary>
    public bool IsGlobalAdmin { get; set; }
}
