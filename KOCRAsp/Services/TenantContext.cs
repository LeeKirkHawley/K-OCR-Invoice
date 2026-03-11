using K_OCR.Data;
using K_OCR.Security;
using KOCRAsp.Security;

namespace KOCRAsp.Services;

/// <summary>
/// Reads the current tenant from the authenticated user's claims.
/// Claims are baked into the auth cookie at sign-in by ApplicationUserClaimsPrincipalFactory
/// and are available on every request via IHttpContextAccessor.
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public TenantContext(IHttpContextAccessor http) => _http = http;

    private System.Security.Claims.ClaimsPrincipal? User => _http.HttpContext?.User;

    public string? OrganizationId =>
        User?.FindFirst(AppClaimTypes.OrganizationId)?.Value;

    public string? OrganizationName =>
        User?.FindFirst(AppClaimTypes.TenantName)?.Value;

    public bool IsSuperAdmin =>
        User?.IsInRole(RoleNames.SuperAdmin) ?? false;
}
