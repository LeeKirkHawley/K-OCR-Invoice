using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using K_OCR_API.Models.OrganizationAdmin.Requests;
using K_OCR_API.Models.OrganizationAdmin.Responses;
using K_OCR_API.Services;
using K_OCR.Security;

namespace K_OCR_API.Controllers;

[ApiController]
[Route("api/organization")]
[Authorize(Policy = "RequireTenantId")]
[Authorize(Roles = RoleNames.OrganizationAdmin + "," + RoleNames.SuperAdmin)]
public class OrganizationAdminController : ControllerBase
{
    private readonly IOrganizationAdminService _organizationAdminService;

    public OrganizationAdminController(IOrganizationAdminService organizationAdminService)
    {
        _organizationAdminService = organizationAdminService;
    }

    [HttpGet("users")]
    public async Task<ActionResult<OrganizationUserOverview[]>> GetUsersAsync()
    {
        var tenantId = GetTenantId();
        var users = await _organizationAdminService.ListUsersAsync(tenantId);
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult<InviteUserResult>> InviteUserAsync(InviteUserRequest request)
    {
        var tenantId = GetTenantId();
        var result = await _organizationAdminService.InviteUserAsync(tenantId, request);
        return CreatedAtAction(nameof(GetUsersAsync), null, result);
    }

    private string GetTenantId()
    {
        var claim = User.FindFirst("TenantId");
        if (claim is null || string.IsNullOrWhiteSpace(claim.Value))
            throw new InvalidOperationException("TenantId claim is required.");

        return claim.Value;
    }
}
