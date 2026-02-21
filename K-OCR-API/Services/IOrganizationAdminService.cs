using K_OCR_API.Models.OrganizationAdmin.Requests;
using K_OCR_API.Models.OrganizationAdmin.Responses;

namespace K_OCR_API.Services;

public interface IOrganizationAdminService
{
    Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId);
    Task<InviteUserResult> InviteUserAsync(string organizationId, InviteUserRequest request);
}
