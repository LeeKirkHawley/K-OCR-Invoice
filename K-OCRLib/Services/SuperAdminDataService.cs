using K_OCR.Data;
using K_OCR.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

/// <summary>
/// Super-admin cross-org data access via Option A (enumerate org folders).
/// Opens each organisation's per-org SQLite database independently — no
/// cross-database SQL JOINs are used.
/// </summary>
public sealed class SuperAdminDataService : ISuperAdminDataService
{
    private readonly ApplicationDbContext _appDb;
    private readonly IPathService _pathService;
    private readonly ILogger<SuperAdminDataService> _logger;

    public SuperAdminDataService(
        ApplicationDbContext appDb,
        IPathService pathService,
        ILogger<SuperAdminDataService> logger)
    {
        _appDb       = appDb;
        _pathService = pathService;
        _logger      = logger;
    }

    public async Task<BatchDetail[]> GetAllBatchesAcrossOrgsAsync()
    {
        // Load the org registry so we can tag each batch with the correct OrganizationId.
        var orgs = await _appDb.Organizations
            .Select(o => new { o.Id, o.Name })
            .ToArrayAsync();

        var results = new List<BatchDetail>();

        foreach (var org in orgs)
        {
            var dbPath = _pathService.GetOrgDbPath(org.Name);
            if (!File.Exists(dbPath))
                continue;

            try
            {
                var opts = new DbContextOptionsBuilder<KOCRDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;

                await using var db = new KOCRDbContext(opts);
                await db.Database.MigrateAsync();

                var batches = await db.Batches.ToListAsync();
                foreach (var b in batches)
                {
                    var fileCount      = await db.Invoices.CountAsync(i => i.BatchId == b.BatchId);
                    var validatedCount = await db.Invoices.CountAsync(i => i.BatchId == b.BatchId && i.IsValidationAccepted);

                    results.Add(new BatchDetail
                    {
                        BatchId                = b.BatchId,
                        OrganizationId         = org.Id,
                        OrganizationName       = org.Name,
                        Name                   = b.Name,
                        BatchNumber            = b.BatchNumber,
                        FolderPath             = b.FolderPath,
                        LockedByUserId         = b.LockedByUserId,
                        LockAcquiredAtUtc      = b.LockAcquiredAtUtc,
                        CreatedAtUtc           = b.CreatedAtUtc,
                        CreatedByUserId        = b.CreatedByUserId,
                        MarkedForDeletionAtUtc = b.MarkedForDeletionAtUtc,
                        FileCount              = fileCount,
                        ValidatedCount         = validatedCount,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read batches from per-org database for org {OrgName} at {DbPath}.", org.Name, dbPath);
            }
        }

        return [.. results.OrderBy(b => b.OrganizationId).ThenBy(b => b.BatchNumber)];
    }

    public async Task<SuperAdminUserDetail[]> GetAllUsersAsync()
    {
        var users = await _appDb.Users
            .AsNoTracking()
            .Select(u => new SuperAdminUserDetail
            {
                UserId = u.Id,
                UserName = u.UserName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName ?? string.Empty,
                OrganizationId = u.OrganizationId,
                OrganizationName = u.Organization != null ? u.Organization.Name : null,
                IsGlobalAdmin = u.IsGlobalAdmin
            })
            .ToArrayAsync();

        return users;
    }
}
