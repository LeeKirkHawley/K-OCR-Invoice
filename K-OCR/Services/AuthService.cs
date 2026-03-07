using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using K_OCR.Identity;
using K_OCR.Models.Api.Auth;

namespace K_OCR.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            throw new InvalidOperationException("Email or password is invalid.");

        if (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Account is locked. Contact your administrator.");

        var roles = await _userManager.GetRolesAsync(user);

        return new LoginResponse
        {
            Token = string.Empty, // No JWT needed for Blazor Server
            ExpiresAtUtc = DateTime.UtcNow.AddHours(8),
            TenantId = user.OrganizationId,
            TenantName = user.Organization?.Name ?? (user.IsGlobalAdmin ? "Global" : null),
            Roles = roles.ToArray(),
            Email = user.Email ?? string.Empty
        };
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task SetPasswordAsync(string userId, string encodedToken, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            throw new InvalidOperationException("Invitation link is invalid or has expired.");

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
    }
}
