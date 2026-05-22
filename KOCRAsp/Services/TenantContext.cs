using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Security;
using K_OCR.Services;

namespace KOCRAsp.Services;

/// <summary>
/// Reads the current tenant from the authenticated user's claims.
/// Claims are baked into the auth cookie at sign-in by ApplicationUserClaimsPrincipalFactory
/// and are available on every request via IHttpContextAccessor.
/// Other front-ends provide their own <see cref="ITenantContext"/> implementations.
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    private readonly ApplicationDbContext _db;

    private Organization? _org;
    private bool _orgLoaded;

    public TenantContext(IHttpContextAccessor http, ApplicationDbContext db)
    {
        _http = http;
        _db   = db;
    }

    private System.Security.Claims.ClaimsPrincipal? User => _http.HttpContext?.User;

    public string? OrganizationId =>
        User?.FindFirst(AppClaimTypes.OrganizationId)?.Value;

    public string? OrganizationName =>
        User?.FindFirst(AppClaimTypes.TenantName)?.Value;

    public bool IsSuperAdmin =>
        User?.IsInRole(RoleNames.SuperAdmin) ?? false;

    public string? StripeCustomerId => GetOrg()?.StripeCustomerId;

    public string StripeSubscriptionStatus => GetOrg()?.StripeSubscriptionStatus ?? "none";

    public bool IsGuestOrganization => GetOrg()?.IsGuestOrganization ?? false;

    public Organization? Organization => GetOrg();

    private Organization? GetOrg()
    {
        if (_orgLoaded) return _org;
        _orgLoaded = true;
        if (OrganizationId is not null)
            _org = _db.Organizations.Find(OrganizationId);
        return _org;
    }
}
