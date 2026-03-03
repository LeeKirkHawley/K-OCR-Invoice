using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using K_OCR.Identity;
using K_OCR.Models.Api.Auth;

namespace K_OCR.Pages.Account;

/// <summary>
/// HTTP endpoint that sets the Identity auth cookie.
/// Called via fetch() from the Blazor login dialog; returns JSON so the
/// circuit can populate WorkspaceState immediately without a full page reload.
/// </summary>
[IgnoreAntiforgeryToken] // Login requests prove identity via credentials, not a form token.
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LoginModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    // GET /account/login — redirect to home; this endpoint exists only for cookie redirect
    // fallback when [Authorize] is added to routes (Step 8 of the identity plan).
    public IActionResult OnGet(string? returnUrl = null) =>
        Redirect(returnUrl ?? "/");

    /// <summary>
    /// POST /account/login — validates credentials and writes the auth cookie.
    /// Returns the full user profile on success so the Blazor circuit can populate
    /// WorkspaceState without a separate round-trip.
    /// </summary>
    public async Task<IActionResult> OnPostAsync([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return new JsonResult(new { success = false, error = "Email and password are required." });

        // UserName may differ from Email (e.g. super-admin uses "superadmin" as UserName).
        // Load the Organization nav property in the same query so tenant info is available.
        var normalizedEmail = _signInManager.UserManager.NormalizeEmail(request.Email);
        var user = await _signInManager.UserManager.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user is null)
            return new JsonResult(new { success = false, error = "Email or password is invalid." });

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var roles = await _signInManager.UserManager.GetRolesAsync(user);
            return new JsonResult(new
            {
                success    = true,
                email      = user.Email,
                tenantId   = user.OrganizationId,
                tenantName = user.Organization?.Name ?? (user.IsGlobalAdmin ? "Global" : null),
                roles      = roles
            });
        }

        if (result.IsLockedOut)
            return new JsonResult(new { success = false, error = "Account is locked. Contact your administrator." });

        return new JsonResult(new { success = false, error = "Email or password is invalid." });
    }
}
