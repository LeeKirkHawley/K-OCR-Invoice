using System.Security.Claims;

namespace K_OCR.Security;

public static class ClaimsPrincipalExtensions
{
    public static string? ActiveOrganizationRole(this ClaimsPrincipal principal) =>
        principal.FindFirst(AppClaimTypes.ActiveOrganizationRole)?.Value;

    public static bool IsActiveOrganizationAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(RoleNames.SuperAdmin) ||
        string.Equals(
            principal.ActiveOrganizationRole(),
            RoleNames.OrganizationAdmin,
            StringComparison.OrdinalIgnoreCase);
}
