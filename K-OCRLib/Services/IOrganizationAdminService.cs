using K_OCR.Models.Api.OrganizationAdmin;

namespace K_OCR.Services;

public interface IOrganizationAdminService
{
    Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId);
    Task<OrganizationUserOverview[]> GetOrgUsersAsync(string organizationId);
    /// <param name="baseUrl">
    /// Scheme+host used to build the invitation setup link, e.g. "https://myapp.example.com".
    /// Leave null or empty to omit the setup link from the result.
    /// </param>
    Task<InviteUserResult> InviteUserAsync(string organizationId, InviteUserRequest request, string? baseUrl = null);
    Task RemoveUserAsync(string userId, string organizationId);
    Task ChangeUserRoleAsync(string userId, string organizationId, string newRole);
}
