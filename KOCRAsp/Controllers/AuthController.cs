using System.Security.Claims;
using KOCRAsp.Models;
using Microsoft.AspNetCore.Authorization;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using K_OCRLib.Services.Interfaces;
using K_OCRLib.Identity;
using K_OCRLib.Models.Api.SuperAdmin;
using K_OCRLib.Security;
using K_OCRLib.Data;
using K_OCRLib.Services;

namespace KOCRAsp.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthService _authSvc;
    private readonly IEmailService _emailSvc;
    private readonly ISuperAdminService _superAdminSvc;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthService authSvc,
        IEmailService emailSvc,
        ISuperAdminService superAdminSvc,
        ILogger<AuthController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _authSvc = authSvc;
        _emailSvc = emailSvc;
        _superAdminSvc = superAdminSvc;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GuestExpiryStatus()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId);
        if (string.IsNullOrWhiteSpace(orgId))
            return Json(new { isPendingDeletion = false });

        var isPending = await _superAdminSvc.IsOrgMarkedForDeletionAsync(orgId);
        return Json(new { isPendingDeletion = isPending });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchOrganization(string orgId, string? returnUrl = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(orgId))
            return Forbid();

        var hasMembership = await _userManager.Users
            .Where(u => u.Id == user.Id)
            .SelectMany(u => u.OrganizationMemberships)
            .AnyAsync(m => m.OrganizationId == orgId && m.Organization.IsActive);

        if (!hasMembership)
            return Forbid();

        user.OrganizationId = orgId;
        await _userManager.UpdateAsync(user);
        await _signInManager.RefreshSignInAsync(user);
        HttpContext.Session.Remove("CurrentBatchId");

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // If user is still authenticated, verify the org is still valid.
            // If not, sign out first before showing login page.
            var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId);
            if (!string.IsNullOrWhiteSpace(orgId))
            {
                try
                {
                    var appDb = HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var org = await appDb.Organizations.FindAsync(orgId);
                    if (org == null || !org.IsActive)
                    {
                        _logger.LogWarning(
                            "Org {OrgId} not found/inactive in login check; signing out.",
                            orgId);
                        await _signInManager.SignOutAsync();
                        HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                    }
                    else
                    {
                        // Org is valid, redirect back to home
                        return returnUrl is not null
                            ? Redirect(returnUrl)
                            : RedirectToAction("Index", "Home");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating org in login check");
                    await _signInManager.SignOutAsync();
                    HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                }
            }
            else
            {
                // No org claim; super admin or other scenario - redirect to home
                return returnUrl is not null
                    ? Redirect(returnUrl)
                    : RedirectToAction("Index", "Home");
            }
        }

        ViewData["ReturnUrl"] = returnUrl;
        ViewData["HideNav"] = true;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["HideNav"] = true;

        if (!ModelState.IsValid)
            return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, isPersistent: false, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            // PasswordSignInAsync matches by UserName; if that failed, try looking up
            // the user by email and sign in by their actual UserName.
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
                result = await _signInManager.PasswordSignInAsync(
                    user.UserName!, model.Password, isPersistent: false, lockoutOnFailure: false);
        }

        if (result.Succeeded)
        {
            _logger.LogInformation("User {Email} logged in.", model.Email);
            var stripeResult = await SyncStripeStatusOnLoginAsync(model.Email);
            if (stripeResult.IsApplicable && stripeResult.Status is not "active" and not "trialing")
                TempData["StripeInactiveWarning"] = true;
            return returnUrl is not null
                ? Redirect(returnUrl)
                : RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked. Please try again later.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        ViewData["HideNav"] = true;
        return View(new ForgotPasswordViewModel());
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        await _signInManager.SignOutAsync();
        TempData["SuccessMessage"] = "Password changed successfully. Please log in.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewData["HideNav"] = true;

        if (!ModelState.IsValid)
            return View(model);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var resetLink = await _authSvc.GeneratePasswordResetLinkAsync(model.Email, baseUrl);

        if (resetLink is not null)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                var displayName = user?.FullName ?? user?.Email ?? model.Email;
                await _emailSvc.SendPasswordResetAsync(model.Email, displayName, resetLink);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", model.Email);
            }
        }

        // Always show the same message to avoid revealing whether the email exists.
        TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult SetPassword(string userId, string token)
    {
        ViewData["HideNav"] = true;
        return View(new SetPasswordViewModel { UserId = userId, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
    {
        ViewData["HideNav"] = true;

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            await _authSvc.SetPasswordAsync(model.UserId, model.Token, model.NewPassword);
            TempData["SuccessMessage"] = "Password set successfully. Please log in.";
            return RedirectToAction(nameof(Login));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetPassword failed for user {UserId}", model.UserId);
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> NextGuestNo()
    {
        var next = await _superAdminSvc.PeekNextGuestNumberAsync();
        return Json(new { nextNo = next });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GuestLogin([FromBody] GuestLoginRequest request)
    {
        try
        {
            var guest = await _superAdminSvc.CreateGuestAsync(request.Email);
            var user = await _userManager.FindByIdAsync(guest.UserId);
            if (user is null)
                return Json(new { success = false, error = "Guest user not found after creation." });

            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("Guest {UserName} created and signed in.", guest.UserName);
            return Json(new { success = true, userName = guest.UserName, password = guest.Password });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GuestLogin failed for {Email}", request.Email);
            return Json(new { success = false, error = ex.Message });
        }
    }
    /// <summary>
    /// Syncs the user's org subscription status from Stripe.
    /// Returns <see cref="StripeStatusResult.NotApplicable"/> for guest orgs.
    /// For all real orgs, always returns an applicable result — never throws.
    /// </summary>
    private async Task<StripeStatusResult> SyncStripeStatusOnLoginAsync(string email)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null) return StripeStatusResult.NotApplicable;

            var fullUser = await _userManager.Users
                .Include(u => u.OrganizationMemberships)
                    .ThenInclude(m => m.Organization)
                .FirstOrDefaultAsync(u => u.Id == user.Id);

            var activeOrgId = fullUser?.OrganizationMemberships
                .Where(m => m.Organization.IsActive)
                .Select(m => m.OrganizationId)
                .FirstOrDefault(id => id == fullUser.OrganizationId)
                ?? fullUser?.OrganizationMemberships
                    .Where(m => m.Organization.IsActive)
                    .OrderBy(m => m.Organization.Name)
                    .Select(m => m.OrganizationId)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(activeOrgId)) return StripeStatusResult.NotApplicable;
            return await _superAdminSvc.SyncStripeStatusAsync(activeOrgId);
        }
        catch (Exception ex)
        {
            // DB or other infrastructure failure — treat as inactive so the user is informed.
            _logger.LogWarning(ex, "Stripe status sync on login failed for {Email}.", email);
            return new StripeStatusResult(true, "error");
        }
    }
}

public sealed class GuestLoginRequest
{
    public string Email { get; set; } = string.Empty;
}
