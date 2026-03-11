using KOCRAsp.Identity;
using KOCRAsp.Models;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthService _authSvc;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthService authSvc,
        ILogger<AuthController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _authSvc = authSvc;
        _logger = logger;
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
}
