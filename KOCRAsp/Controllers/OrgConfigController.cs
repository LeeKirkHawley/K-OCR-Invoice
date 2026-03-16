using System.Security.Claims;
using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Models.Api.SuperAdmin;
using KOCRAsp.Security;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Roles = "OrganizationAdmin,SuperAdmin")]
public class OrgConfigController : Controller
{
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly ITenantContext _tenantContext;
    private readonly ISuperAdminService _superAdminSvc;
    private readonly ILogger<OrgConfigController> _logger;

    public OrgConfigController(
        IOrgConfigService orgConfigSvc,
        ITenantContext tenantContext,
        ISuperAdminService superAdminSvc,
        ILogger<OrgConfigController> logger)
    {
        _orgConfigSvc   = orgConfigSvc;
        _tenantContext  = tenantContext;
        _superAdminSvc  = superAdminSvc;
        _logger         = logger;
    }

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

    private async Task<(string? OrgName, OrganizationOverview[]? AllOrgs, string? SelectedOrgId)> ResolveOrgAsync(string? requestedOrgId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
        {
            var allOrgs = await _superAdminSvc.ListOrganizationsAsync();
            var selected = requestedOrgId is not null
                ? allOrgs.FirstOrDefault(o => o.OrganizationId == requestedOrgId)
                : allOrgs.FirstOrDefault();
            return (selected?.Name, allOrgs, selected?.OrganizationId);
        }

        // Org admin: scope to their own organisation only
        var orgName = _tenantContext.OrganizationName;
        return (orgName, null, null);
    }
}
