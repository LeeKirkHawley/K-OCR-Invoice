using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using K_OCR_API.Identity;
using K_OCR_API.Models.Auth;
using K_OCR_API.Services;

namespace K_OCR_API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user is null)
            return Unauthorized("Email or password is invalid.");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized("Email or password is invalid.");

        var roles = await _userManager.GetRolesAsync(user);
        var tokenResult = _jwtTokenService.GenerateToken(user, roles);

        return Ok(new LoginResponse
        {
            Token = tokenResult.Token,
            ExpiresAtUtc = tokenResult.ExpiresAtUtc,
            TenantId = string.IsNullOrWhiteSpace(user.OrganizationId) ? null : user.OrganizationId,
            TenantName = user.Organization?.Name ?? (user.IsGlobalAdmin ? "Global" : null),
            Roles = roles.ToArray(),
            Email = user.Email ?? string.Empty
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return NotFound("User not found.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        return NoContent();
    }
}
