using KOCRAsp.Models.Api.OrganizationAdmin;

namespace KOCRAsp.Services;

public interface IOrganizationAdminService
{
    Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId);
    Task<OrganizationUserOverview[]> GetOrgUsersAsync(string organizationId);
    Task<InviteUserResult> InviteUserAsync(string organizationId, InviteUserRequest request);
    Task RemoveUserAsync(string userId, string organizationId);
}
