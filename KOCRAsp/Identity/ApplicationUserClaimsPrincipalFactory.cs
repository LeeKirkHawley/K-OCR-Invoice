using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KOCRAsp.Security;

namespace KOCRAsp.Identity;

/// <summary>
/// Extends the default claims principal factory to bake tenant and global-admin
/// information into the auth cookie as custom claims.
///
/// This runs once during <c>SignInManager.PasswordSignInAsync</c>.  The claims are
/// then readable from <c>AuthenticationState</c> on every circuit start without an
/// additional database round-trip, which is what enables <c>MainLayout</c> to
/// re-hydrate <c>WorkspaceState</c> after a page refresh.
/// </summary>
internal sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Load the Organization navigation property — not included by UserManager by default.
        var fullUser = await UserManager.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (!string.IsNullOrEmpty(fullUser?.OrganizationId))
            identity.AddClaim(new Claim(AppClaimTypes.OrganizationId, fullUser.OrganizationId));

        var tenantName = fullUser?.Organization?.Name
                      ?? (fullUser?.IsGlobalAdmin == true ? "Global" : null);

        if (tenantName is not null)
            identity.AddClaim(new Claim(AppClaimTypes.TenantName, tenantName));

        return identity;
    }
}
