using K_OCR.Identity;
using KOCRAsp.Models;
using K_OCR.Services;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GuestExpiryStatus()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.OrganizationId == null)
            return Json(new { isPendingDeletion = false });

        var isPending = await _superAdminSvc.IsOrgMarkedForDeletionAsync(user.OrganizationId);
        return Json(new { isPendingDeletion = isPending });
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");

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
            return Redirect(returnUrl ?? "/");
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
}

public sealed class GuestLoginRequest
{
    public string Email { get; set; } = string.Empty;
}
