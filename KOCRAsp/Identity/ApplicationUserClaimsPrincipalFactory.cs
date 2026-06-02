using System.Security.Claims;
using K_OCRLib.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

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
        await ApplicationUserClaimsExtensions.AddKOCRClaimsAsync(identity, user, UserManager);
        return identity;
    }
}
