using K_OCRLib.Security;

namespace K_OCRLib.Identity;

public class UserOrganizationMembership
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string OrganizationId { get; set; } = string.Empty;
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Org-scoped role for this membership. Expected values are OrganizationAdmin,
    /// OrganizationValidator, or OrganizationUser.
    /// </summary>
    public string Role { get; set; } = RoleNames.OrganizationUser;
}
