using System.Security.Claims;
using K_OCRLib.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace K_OCRLib.Identity;

/// <summary>
/// Shared logic for building K-OCR custom claims from an <see cref="ApplicationUser"/>.
/// Called by <c>ApplicationUserClaimsPrincipalFactory</c> in the ASP.NET host and can
/// be reused by any other front-end that needs the same claims.
/// </summary>
public static class ApplicationUserClaimsExtensions
{
    /// <summary>
    /// Adds K-OCR tenant claims (<c>OrganizationId</c> and <c>TenantName</c>) to
    /// <paramref name="identity"/> for the given user, loading the Organization
    /// navigation property via <paramref name="userManager"/> if needed.
    /// </summary>
    public static async Task AddKOCRClaimsAsync(
        ClaimsIdentity identity,
        ApplicationUser user,
        UserManager<ApplicationUser> userManager)
    {
        var fullUser = await userManager.Users
            .Include(u => u.OrganizationMemberships)
                .ThenInclude(m => m.Organization)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        var memberships = fullUser?.OrganizationMemberships
            .Where(m => m.Organization.IsActive)
            .ToList()
            ?? [];

        var activeMembership = memberships.FirstOrDefault(m => m.OrganizationId == fullUser?.OrganizationId)
            ?? memberships
                .OrderBy(m => m.Organization.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (activeMembership is not null)
        {
            identity.AddClaim(new Claim(AppClaimTypes.OrganizationId, activeMembership.OrganizationId));
            identity.AddClaim(new Claim(AppClaimTypes.TenantName, activeMembership.Organization.Name));
            identity.AddClaim(new Claim(AppClaimTypes.ActiveOrganizationRole, activeMembership.Role));
            return;
        }

        var tenantName = fullUser?.IsGlobalAdmin == true ? "Global" : null;

        if (tenantName is not null)
            identity.AddClaim(new Claim(AppClaimTypes.TenantName, tenantName));
    }
}
