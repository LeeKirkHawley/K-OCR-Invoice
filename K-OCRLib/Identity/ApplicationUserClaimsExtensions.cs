using System.Security.Claims;
using K_OCR.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace K_OCR.Identity;

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
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (!string.IsNullOrEmpty(fullUser?.OrganizationId))
            identity.AddClaim(new Claim(AppClaimTypes.OrganizationId, fullUser.OrganizationId));

        var tenantName = fullUser?.Organization?.Name
                      ?? (fullUser?.IsGlobalAdmin == true ? "Global" : null);

        if (tenantName is not null)
            identity.AddClaim(new Claim(AppClaimTypes.TenantName, tenantName));
    }
}
