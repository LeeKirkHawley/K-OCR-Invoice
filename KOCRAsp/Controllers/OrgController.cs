using System.Security.Claims;
using K_OCR.Security;
using KOCRAsp.Identity;
using KOCRAsp.Models;
using KOCRAsp.Models.Api.OrganizationAdmin;
using KOCRAsp.Security;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Roles = "OrganizationAdmin,SuperAdmin")]
public class OrgController : Controller
{
    private readonly IOrganizationAdminService _orgAdminSvc;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrgController> _logger;

    public OrgController(
        IOrganizationAdminService orgAdminSvc,
        UserManager<ApplicationUser> userManager,
        ILogger<OrgController> logger)
    {
        _orgAdminSvc = orgAdminSvc;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        try
        {
            var result = await _orgAdminSvc.InviteUserAsync(orgId, request);
            return Json(new
            {
                success = true,
                userId = result.UserId,
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
    public async Task<IActionResult> RemoveUser([FromBody] RemoveUserRequest request)
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        try
        {
            await _orgAdminSvc.RemoveUserAsync(request.UserId, orgId);
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
    public async Task<IActionResult> ChangeRole([FromBody] ChangeRoleRequest request)
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        try
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
                return Json(new { success = false, error = "User not found." });

            if (user.OrganizationId != orgId && !User.IsInRole(RoleNames.SuperAdmin))
                return Json(new { success = false, error = "Access denied." });

            var currentRoles = await _userManager.GetRolesAsync(user);
            var orgRoles = new[]
            {
                RoleNames.OrganizationAdmin,
                RoleNames.OrganizationValidator,
                RoleNames.OrganizationUser
            };
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

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var users = await _orgAdminSvc.ListUsersAsync(orgId);
        return Json(users);
    }
}

public sealed class RemoveUserRequest
{
    public string UserId { get; set; } = string.Empty;
}
