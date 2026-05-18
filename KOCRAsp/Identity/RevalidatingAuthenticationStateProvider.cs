using System.Security.Claims;
using K_OCR.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace KOCRAsp.Identity;

/// <summary>
/// Blazor Server authentication state provider that periodically revalidates
/// the signed-in user's security stamp against the database.
///
/// If the stamp has changed — because an admin locked the account, the user
/// changed their password, or the super-admin called UpdateSecurityStampAsync —
/// the circuit is forcibly de-authenticated within the revalidation interval
/// rather than only on the next full page load.
/// </summary>
internal sealed class RevalidatingAuthenticationStateProvider
    : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdentityOptions _options;

    public RevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> optionsAccessor)
        : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _options      = optionsAccessor.Value;
    }

    // 30 minutes balances freshness against DB round-trips on active circuits.
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        // Use a fresh scope so we never share the circuit's scoped DbContext across threads.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
    }

    private async Task<bool> ValidateSecurityStampAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
            return false;

        // If the store doesn't support security stamps every validation passes.
        if (!userManager.SupportsUserSecurityStamp)
            return true;

        var principalStamp = principal.FindFirstValue(_options.ClaimsIdentity.SecurityStampClaimType);
        var userStamp      = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
