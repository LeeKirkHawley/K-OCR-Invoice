using K_OCR_API.Models.SuperAdmin.Requests;
using K_OCR_API.Models.SuperAdmin.Responses;

namespace K_OCR_API.Services;

public interface ISuperAdminService
{
    Task<OrganizationOverview[]> ListOrganizationsAsync();
    Task<CreateOrganizationResult> CreateOrganizationAsync(CreateOrganizationRequest request);
    Task RevokeOrganizationAsync(string organizationId);
}
