using K_OCR.Models.Api.SuperAdmin;

namespace K_OCR.Services;

public interface ISuperAdminService
{
    Task<OrganizationOverview[]> ListOrganizationsAsync();
    Task<CreateOrganizationResult> CreateOrganizationAsync(CreateOrganizationRequest request);
    Task RevokeOrganizationAsync(string organizationId);
    Task ReEnableOrganizationAsync(string organizationId);
    Task DeleteOrganizationAsync(string organizationId);
}
