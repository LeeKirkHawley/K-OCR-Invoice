using K_OCR.Models.Api.SuperAdmin;
using K_OCR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class AdminController : Controller
{
    private readonly ISuperAdminService _superAdminSvc;
    private readonly ISuperAdminDataService _dataSvc;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ISuperAdminService superAdminSvc,
        ISuperAdminDataService dataSvc,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _superAdminSvc = superAdminSvc;
        _dataSvc = dataSvc;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orgs = await _superAdminSvc.ListOrganizationsAsync();
        var batches = await _dataSvc.GetAllBatchesAcrossOrgsAsync();
        ViewBag.Batches = batches;
        int value = 0;
        try
        {
            value = _configuration.GetValue<int>("DeletedOrgRetentionDays");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read DeletedOrgRetentionDays from configuration. Defaulting to 14 days.");
            value = 14; // default fallback
        }
        ViewBag.DeletedOrgRetentionDays = value;
        return View(orgs);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _superAdminSvc.CreateOrganizationAsync(request, baseUrl);
            return Json(new
            {
                success = true,
                orgId = result.OrganizationId,
                adminEmail = result.AdminEmail,
                setupLink = result.SetupLink,
                emailSent = result.EmailSent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateOrganization failed for {Name}", request.Name);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RevokeOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.RevokeOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RevokeOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReEnableOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.ReEnableOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReEnableOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.MarkOrganizationForDeletionAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkOrganizationForDeletion failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReinstateOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.ReinstateMarkedOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReinstateOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> HardDeleteOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.DeleteOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HardDeleteOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetUserPassword([FromBody] SetUserPasswordRequest request)
    {
        try
        {
            var user = await GetUserManagerAsync(request.UserId);
            if (user == null)
                return Json(new { success = false, error = "User not found." });

            var token = await user.UserManager.GeneratePasswordResetTokenAsync(user.User);
            var result = await user.UserManager.ResetPasswordAsync(user.User, token, request.NewPassword);
            if (!result.Succeeded)
                return Json(new { success = false, error = string.Join("; ", result.Errors.Select(e => e.Description)) });

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetUserPassword failed for {UserId}", request.UserId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllBatches()
    {
        var batches = await _dataSvc.GetAllBatchesAcrossOrgsAsync();
        return Json(batches);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ProvisionStripe([FromBody] ProvisionStripeRequest request)
    {
        try
        {
            await _superAdminSvc.ProvisionStripeAsync(request.OrgId, string.IsNullOrWhiteSpace(request.PriceId) ? null : request.PriceId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProvisionStripe failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // Helper — resolves lazily to avoid circular ctor dependency
    private Task<UserManagerContext?> GetUserManagerAsync(string userId)
    {
        var userManager = HttpContext.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<K_OCR.Identity.ApplicationUser>>();
        return ResolveUser(userManager, userId);
    }

    private static async Task<UserManagerContext?> ResolveUser(
        Microsoft.AspNetCore.Identity.UserManager<K_OCR.Identity.ApplicationUser> mgr, string userId)
    {
        var user = await mgr.FindByIdAsync(userId);
        return user is null ? null : new UserManagerContext(mgr, user);
    }

    private sealed record UserManagerContext(
        Microsoft.AspNetCore.Identity.UserManager<K_OCR.Identity.ApplicationUser> UserManager,
        K_OCR.Identity.ApplicationUser User);
}

// Local request DTOs
public sealed class OrgIdRequest
{
    public string OrgId { get; set; } = string.Empty;
}

public sealed class SetUserPasswordRequest
{
    public string UserId { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
