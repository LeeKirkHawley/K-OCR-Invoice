using K_OCR.Data;
using K_OCR.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public interface IBatchActionService
{
    Task LogAsync(string action, string batchName, string organization, string orgUser);
}

public class BatchActionService : IBatchActionService
{
    private readonly IDbContextFactory<KOCRDbContext> _dbFactory;
    private readonly ILogger<BatchActionService> _logger;

    public BatchActionService(IDbContextFactory<KOCRDbContext> dbFactory, ILogger<BatchActionService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    public async Task LogAsync(string action, string batchName, string organization, string orgUser)
    {
        try
        {
            await using var db = _dbFactory.CreateDbContext();
            db.BatchActions.Add(new BatchAction
            {
                Action       = action,
                TimestampUtc = DateTime.UtcNow,
                Organization = organization,
                OrgUser      = orgUser,
                BatchName    = batchName,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Logging failure must never abort the primary operation.
            _logger.LogError(ex, "Failed to write BatchAction: Action={Action}, Batch={BatchName}, Org={Org}, User={User}",
                action, batchName, organization, orgUser);
        }
    }
}
