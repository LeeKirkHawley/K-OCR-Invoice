using System.Security.Claims;
using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Models;
using K_OCR.Models.Api.OrganizationAdmin;
using K_OCR.Models.Api.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Controllers;

[Authorize(Policy = "OrgAdminOrSuperAdmin")]
public class OrgConfigController : Controller
{
    private static readonly HashSet<string> AllowedOrgRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.OrganizationAdmin,
        RoleNames.OrganizationValidator,
        RoleNames.OrganizationUser
    };

    private readonly IOrgConfigService _orgConfigSvc;
    private readonly ISuperAdminService _superAdminSvc;
    private readonly IOrganizationAdminService _orgAdminSvc;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OrgConfigController> _logger;

    public OrgConfigController(
        ApplicationDbContext dbContext,
        IOrgConfigService orgConfigSvc,
        ISuperAdminService superAdminSvc,
        IOrganizationAdminService orgAdminSvc,
        ILogger<OrgConfigController> logger)
    {
        _orgConfigSvc  = orgConfigSvc;
        _superAdminSvc = superAdminSvc;
        _orgAdminSvc   = orgAdminSvc;
        _dbContext     = dbContext;
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
        var (orgName, _, selectedOrgId) = await ResolveOrgAsync(orgId);

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

        return RedirectToAction(nameof(Index), selectedOrgId is null ? null : new { orgId = selectedOrgId });
    }

    // ── User management API ───────────────────────────────────────────────────

    [HttpGet]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GetUsers(string? orgId = null)
    {
        var resolvedOrgId = await ResolveOrgIdAsync(orgId);
        if (resolvedOrgId is null)
            return Json(Array.Empty<object>());

        var users = await _orgAdminSvc.ListUsersAsync(resolvedOrgId);
        return Json(users);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request, [FromQuery] string? orgId = null)
    {
        var resolvedOrgId = await ResolveOrgIdAsync(orgId);
        if (resolvedOrgId is null)
            return Json(new { success = false, error = "Organization not found." });

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _orgAdminSvc.InviteUserAsync(resolvedOrgId, request, baseUrl);
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
        var resolvedOrgId = await ResolveOrgIdAsync(orgId);
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
        var resolvedOrgId = await ResolveOrgIdAsync(orgId);
        if (resolvedOrgId is null)
            return Json(new { success = false, error = "Organization not found." });
        if (!AllowedOrgRoles.Contains(request.NewRole))
            return Json(new { success = false, error = "Invalid role." });

        try
        {
            await _orgAdminSvc.ChangeUserRoleAsync(request.UserId, resolvedOrgId, request.NewRole);
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
    private async Task<string?> ResolveOrgIdAsync(string? requestedOrgId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return requestedOrgId;

        var candidateOrgId = requestedOrgId ?? User.FindFirstValue(AppClaimTypes.OrganizationId);
        if (string.IsNullOrWhiteSpace(candidateOrgId))
            return null;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return await _dbContext.UserOrganizationMemberships
            .Where(m =>
                m.UserId == userId &&
                m.OrganizationId == candidateOrgId &&
                m.Organization.IsActive &&
                m.Role == RoleNames.OrganizationAdmin)
            .Select(m => m.OrganizationId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Resolves the org name (and optional list for SuperAdmin) for page rendering.
    /// </summary>
    private async Task<(string? OrgName, OrganizationOverview[] AllOrgs, string? SelectedOrgId)> ResolveOrgAsync(string? requestedOrgId)
    {
        OrganizationOverview[] allOrgs;

        if (User.IsInRole(RoleNames.SuperAdmin))
        {
            allOrgs = await _superAdminSvc.ListOrganizationsAsync();
        }
        else
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return (null, [], null);

            allOrgs = await _dbContext.UserOrganizationMemberships
                .AsNoTracking()
                .Where(m =>
                    m.UserId == userId &&
                    m.Organization.IsActive &&
                    m.Role == RoleNames.OrganizationAdmin)
                .OrderBy(m => m.Organization.Name)
                .Select(m => new OrganizationOverview
                {
                    OrganizationId = m.OrganizationId,
                    Name = m.Organization.Name,
                    Description = m.Organization.Description,
                    IsActive = m.Organization.IsActive,
                    IsGuestOrganization = m.Organization.IsGuestOrganization,
                    MarkedForDeletionAtUtc = m.Organization.MarkedForDeletionAtUtc,
                    CreatedAtUtc = m.Organization.CreatedAtUtc
                })
                .ToArrayAsync();
        }

        if (allOrgs.Length == 0)
            return (null, [], null);

        var activeClaimOrgId = User.FindFirstValue(AppClaimTypes.OrganizationId);
        var selectedOrgId = requestedOrgId is not null && allOrgs.Any(o => o.OrganizationId == requestedOrgId)
            ? requestedOrgId
            : activeClaimOrgId is not null && allOrgs.Any(o => o.OrganizationId == activeClaimOrgId)
                ? activeClaimOrgId
                : allOrgs[0].OrganizationId;

        var selected = allOrgs.FirstOrDefault(o => o.OrganizationId == selectedOrgId);
        return (selected?.Name, allOrgs, selected?.OrganizationId);
    }
}
