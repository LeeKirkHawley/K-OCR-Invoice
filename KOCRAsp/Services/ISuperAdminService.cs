using KOCRAsp.Models.Api.SuperAdmin;

namespace KOCRAsp.Services;

public interface ISuperAdminService
{
    Task<OrganizationOverview[]> ListOrganizationsAsync();
    Task<CreateOrganizationResult> CreateOrganizationAsync(CreateOrganizationRequest request);
    Task RevokeOrganizationAsync(string organizationId);
    Task ReEnableOrganizationAsync(string organizationId);
    Task DeleteOrganizationAsync(string organizationId);
    Task<int> PeekNextGuestNumberAsync();
    Task<GuestLoginResult> CreateGuestAsync(string email);
}
