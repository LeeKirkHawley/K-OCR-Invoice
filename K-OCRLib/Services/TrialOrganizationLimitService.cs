using K_OCRLib.Data;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Services;

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
        var isBeta = _tenantContext.IsBetaTestOrganization;
        var isGuest = _tenantContext.IsGuestOrganization;

        if (!isBeta && !isGuest)
            return new TrialOrganizationLimitStatus(false, false, 0, 0, 0, 0);

        // the database context we're getting here is the sqlite per-org dabase
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Track beta: OCR pages
        int maxBatches = 0;
        int usedBatches = 0;
        int maxOcrPages = 0;
        int usedOcrPages = await db.Invoices
            .Where(i => i.IsFullyProcessed && i.ProcessedAtUtc != null)
            .SumAsync(i => (int?)i.TotalPages) ?? 0;

        if (isBeta)
        {
            maxOcrPages = _configSvc.GetBetaMaxOcrPages();
            var organizationId = _tenantContext.OrganizationId;
            if (!string.IsNullOrWhiteSpace(organizationId))
            {
                var orgMaxPages = await _appDbContext.Organizations
                    .Where(o => o.Id == organizationId)
                    .Select(o => (int?)o.BetaMaxOcrPages)
                    .FirstOrDefaultAsync();
                if (orgMaxPages is > 0)
                    maxOcrPages = orgMaxPages.Value;
            }

            //usedOcrPages = await db.Invoices
            //    .Where(i => i.IsFullyProcessed && i.ProcessedAtUtc != null)
            //    .SumAsync(i => (int?)i.TotalPages) ?? 0;
        }

        // Track guest: active batches
        if (isGuest)
        {
            maxBatches = _configSvc.GetGuestMaxBatches();

            maxOcrPages = _configSvc.GetMaxPagesPerInvoice(isGuest);

            usedBatches = await db.Batches
                //.Where(b => b.MarkedForDeletionAtUtc == null)
                .CountAsync();
        }

        return new TrialOrganizationLimitStatus(
            isBeta,
            isGuest,
            maxOcrPages,
            usedOcrPages,
            maxBatches,
            usedBatches);
    }
}
