using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using K_OCR.Identity;

namespace K_OCR.Pages.Account;

/// <summary>
/// HTTP endpoint that clears the Identity auth cookie.
/// Reached via NavigationManager.NavigateTo("/account/logout", forceLoad: true)
/// from the Blazor circuit, which issues a real GET request that can write headers.
/// </summary>
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/");
    }
}
