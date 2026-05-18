using K_OCR.Models.Api.SuperAdmin;

namespace K_OCR.Services;

public interface ISuperAdminService
{
    Task<OrganizationOverview[]> ListOrganizationsAsync();
    /// <param name="baseUrl">
    /// Scheme+host used to build the admin invitation setup link, e.g. "https://myapp.example.com".
    /// Leave null or empty to omit the setup link.
    /// </param>
    Task<CreateOrganizationResult> CreateOrganizationAsync(CreateOrganizationRequest request, string? baseUrl = null);
    Task RevokeOrganizationAsync(string organizationId);
    Task ReEnableOrganizationAsync(string organizationId);
    /// <summary>Hard-deletes an org immediately. Org must already be revoked (IsActive=false).</summary>
    Task DeleteOrganizationAsync(string organizationId);
    /// <summary>Marks a revoked org for soft-deletion and locks its users.</summary>
    Task MarkOrganizationForDeletionAsync(string organizationId);
    /// <summary>Removes the deletion mark and re-enables the org and its users.</summary>
    Task ReinstateMarkedOrganizationAsync(string organizationId);
    /// <summary>Hard-deletes all orgs whose MarkedForDeletionAtUtc has passed the retention window.</summary>
    Task<int> CleanupExpiredSoftDeletesAsync(TimeSpan retention, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> CleanupExpiredGuestAccountsAsync(TimeSpan retention, CancellationToken cancellationToken = default);
    Task<bool> IsOrgMarkedForDeletionAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<int> PeekNextGuestNumberAsync();
    Task<GuestLoginResult> CreateGuestAsync(string email);
    /// <summary>Creates a Stripe Customer + Subscription for the given org and saves the IDs.</summary>
    Task ProvisionStripeAsync(string organizationId, string? priceId = null);
}
