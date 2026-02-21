using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using K_OCR_API.Models.SuperAdmin.Requests;
using K_OCR_API.Models.SuperAdmin.Responses;
using K_OCR_API.Services;

namespace K_OCR_API.Controllers;

[ApiController]
[Route("api/super-admin")]
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminController : ControllerBase
{
    private readonly ISuperAdminService _superAdminService;

    public SuperAdminController(ISuperAdminService superAdminService)
    {
        _superAdminService = superAdminService;
    }

    [HttpGet("organizations")]
    public async Task<ActionResult<OrganizationOverview[]>> GetOrganizationsAsync()
    {
        var orgs = await _superAdminService.ListOrganizationsAsync();
        return Ok(orgs);
    }

    [HttpPost("organizations")]
    public async Task<ActionResult<CreateOrganizationResult>> CreateOrganizationAsync(CreateOrganizationRequest request)
    {
        var result = await _superAdminService.CreateOrganizationAsync(request);
        return CreatedAtAction(nameof(GetOrganizationsAsync), new { id = result.OrganizationId }, result);
    }

    [HttpPost("organizations/{organizationId}/revoke")]
    public async Task<IActionResult> RevokeOrganizationAsync(string organizationId)
    {
        await _superAdminService.RevokeOrganizationAsync(organizationId);
        return NoContent();
    }
}
