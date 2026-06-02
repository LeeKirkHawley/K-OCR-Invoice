using K_OCRLib.Data;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Services;

public sealed record TrialOrganizationLimitStatus(
    bool IsBetaTestOrganization,
    int MaxOcrPages,
    int UsedOcrPages)
{
    public int RemainingOcrPages => Math.Max(0, MaxOcrPages - UsedOcrPages);
    public bool IsLimitExceeded => UsedOcrPages >= MaxOcrPages;
}

public interface ITrialOrganizationLimitService
{
    Task<TrialOrganizationLimitStatus> GetCurrentStatusAsync();
}

public sealed class TrialOrganizationLimitService : ITrialOrganizationLimitService
{
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _appDbContext;
    private readonly IDbContextFactory<KOCRDbContext> _dbFactory;
    private readonly IConfigurationService _configSvc;

    public TrialOrganizationLimitService(
        ITenantContext tenantContext,
        ApplicationDbContext appDbContext,
        IDbContextFactory<KOCRDbContext> dbFactory,
        IConfigurationService configSvc)
    {
        _tenantContext = tenantContext;
        _appDbContext = appDbContext;
        _dbFactory = dbFactory;
        _configSvc = configSvc;
    }

    public async Task<TrialOrganizationLimitStatus> GetCurrentStatusAsync()
    {
        var configuredDefault = _configSvc.GetBetaMaxOcrPages();
        if (!_tenantContext.IsBetaTestOrganization)
            return new TrialOrganizationLimitStatus(false, configuredDefault, 0);

        var maxPages = configuredDefault;
        var organizationId = _tenantContext.OrganizationId;
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var orgMaxPages = await _appDbContext.Organizations
                .Where(o => o.Id == organizationId)
                .Select(o => (int?)o.BetaMaxOcrPages)
                .FirstOrDefaultAsync();
            if (orgMaxPages is > 0)
                maxPages = orgMaxPages.Value;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var usedPages = await db.Invoices
            .Where(i => i.IsFullyProcessed && i.ProcessedAtUtc != null)
            .SumAsync(i => (int?)i.TotalPages) ?? 0;

        return new TrialOrganizationLimitStatus(true, maxPages, usedPages);
    }
}
