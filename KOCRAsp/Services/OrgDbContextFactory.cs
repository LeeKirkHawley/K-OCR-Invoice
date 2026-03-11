using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Services;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Services;

/// <summary>
/// Scoped factory that opens a <see cref="KOCRDbContext"/> for the current org user.
/// Options (and the org-routing check) are deferred to <see cref="CreateDbContext"/>,
/// so injecting this factory into a super-admin scope does not throw.
/// </summary>
internal sealed class OrgDbContextFactory : IDbContextFactory<KOCRDbContext>
{
    private readonly ITenantContext _tenant;
    private readonly IPathService _paths;
    private readonly DatabaseSettings _settings;

    public OrgDbContextFactory(
        ITenantContext tenant,
        IPathService paths,
        DatabaseSettings settings)
    {
        _tenant   = tenant;
        _paths    = paths;
        _settings = settings;
    }

    /// <summary>
    /// Creates a new <see cref="KOCRDbContext"/> for the authenticated org user.
    /// Throws <see cref="InvalidOperationException"/> if called from a super-admin
    /// or unauthenticated scope.
    /// </summary>
    public KOCRDbContext CreateDbContext()
    {
        if (_tenant.IsSuperAdmin)
            throw new InvalidOperationException(
                "KOCRDbContext cannot be used in a super-admin context. " +
                "Use SuperAdminDataService for cross-org operations.");

        var dbPath = _tenant.OrganizationName is not null
            ? _paths.GetOrgDbPath(_tenant.OrganizationName)
            : throw new InvalidOperationException(
                "KOCRDbContext requires an authenticated org user.");

        var optionsBuilder = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}");

        if (_settings.EnableSensitiveDataLogging)
            optionsBuilder.EnableSensitiveDataLogging();
        if (_settings.EnableDetailedErrors)
            optionsBuilder.EnableDetailedErrors();

        return new KOCRDbContext(optionsBuilder.Options);
    }
}
