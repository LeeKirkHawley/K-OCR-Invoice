using System.Security.Claims;
using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Identity;
using KOCRAsp.Models;
using KOCRAsp.Models.Api.OrganizationAdmin;
using KOCRAsp.Models.Api.SuperAdmin;
using KOCRAsp.Security;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Roles = "OrganizationAdmin,SuperAdmin")]
public class OrgConfigController : Controller
{
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly ITenantContext _tenantContext;
    private readonly ISuperAdminService _superAdminSvc;
    private readonly IOrganizationAdminService _orgAdminSvc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrgConfigController> _logger;

    public OrgConfigController(
        IOrgConfigService orgConfigSvc,
        ITenantContext tenantContext,
        ISuperAdminService superAdminSvc,
        IOrganizationAdminService orgAdminSvc,
        UserManager<ApplicationUser> userManager,
        ILogger<OrgConfigController> logger)
    {
        _orgConfigSvc  = orgConfigSvc;
        _tenantContext = tenantContext;
        _superAdminSvc = superAdminSvc;
        _orgAdminSvc   = orgAdminSvc;
        _userManager   = userManager;
        _logger        = logger;
    }

    // ── Page ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(string? orgId = null)
    {
        var (orgName, allOrgs, selectedOrgId) = await ResolveOrgAsync(orgId);

        if (orgName is null)
            return NotFound("Organization not found.");

        var config = await _orgConfigSvc.LoadAsync(orgName);

        ViewBag.AllOrgs       = allOrgs;
        ViewBag.SelectedOrgId = selectedOrgId;
        ViewBag.OrgName       = orgName;

        return View(config);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(OrgConfig model, string? orgId = null)
    {
        var (orgName, _, _) = await ResolveOrgAsync(orgId);

        if (orgName is null)
            return NotFound("Organization not found.");

        try
        {
            await _orgConfigSvc.SaveAsync(orgName, model);
            TempData["Success"] = "Organization configuration saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveOrgConfig failed for org {OrgName}", orgName);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), orgId is null ? null : new { orgId });
    }

    // ── User management API ───────────────────────────────────────────────────

    [HttpGet]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GetUsers(string? orgId = null)
    {
        var resolvedOrgId = ResolveOrgId(orgId);
        if (resolvedOrgId is null)
            return Json(Array.Empty<object>());

        var users = await _orgAdminSvc.ListUsersAsync(resolvedOrgId);
        return Json(users);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request, [FromQuery] string? orgId = null)
    {
        var resolvedOrgId = ResolveOrgId(orgId);
        if (resolvedOrgId is null)
            return Json(new { success = false, error = "Organization not found." });

        try
        {
            var result = await _orgAdminSvc.InviteUserAsync(resolvedOrgId, request);
            return Json(new
            {
                success   = true,
                userId    = result.UserId,
                emailSent = result.EmailSent,
                setupLink = result.SetupLink
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InviteUser failed for {Email}", request.Email);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RemoveUser([FromBody] RemoveUserRequest request, [FromQuery] string? orgId = null)
    {
        var resolvedOrgId = ResolveOrgId(orgId);
        if (resolvedOrgId is null)
            return Json(new { success = false, error = "Organization not found." });

        try
        {
            await _orgAdminSvc.RemoveUserAsync(request.UserId, resolvedOrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveUser failed for {UserId}", request.UserId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ChangeRole([FromBody] ChangeRoleRequest request, [FromQuery] string? orgId = null)
    {
        var resolvedOrgId = ResolveOrgId(orgId);
        if (resolvedOrgId is null)
            return Json(new { success = false, error = "Organization not found." });

        try
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user is null)
                return Json(new { success = false, error = "User not found." });

            if (user.OrganizationId != resolvedOrgId && !User.IsInRole(RoleNames.SuperAdmin))
                return Json(new { success = false, error = "Access denied." });

            var currentRoles = await _userManager.GetRolesAsync(user);
            var orgRoles = new[] { RoleNames.OrganizationAdmin, RoleNames.OrganizationValidator, RoleNames.OrganizationUser };
            var rolesToRemove = currentRoles.Where(r => orgRoles.Contains(r)).ToList();
            if (rolesToRemove.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, rolesToRemove);

            await _userManager.AddToRoleAsync(user, request.NewRole);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChangeRole failed for {UserId}", request.UserId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the orgId string to use for API calls.
    /// SuperAdmin: uses the provided <paramref name="requestedOrgId"/>.
    /// OrgAdmin: always uses their own org from claims.
    /// </summary>
    private string? ResolveOrgId(string? requestedOrgId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return requestedOrgId;

        return User.FindFirstValue(AppClaimTypes.OrganizationId);
    }

    /// <summary>
    /// Resolves the org name (and optional list for SuperAdmin) for page rendering.
    /// </summary>
    private async Task<(string? OrgName, OrganizationOverview[]? AllOrgs, string? SelectedOrgId)> ResolveOrgAsync(string? requestedOrgId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
        {
            var allOrgs  = await _superAdminSvc.ListOrganizationsAsync();
            var selected = requestedOrgId is not null
                ? allOrgs.FirstOrDefault(o => o.OrganizationId == requestedOrgId)
                : allOrgs.FirstOrDefault();
            return (selected?.Name, allOrgs, selected?.OrganizationId);
        }

        return (_tenantContext.OrganizationName, null, null);
    }
}
