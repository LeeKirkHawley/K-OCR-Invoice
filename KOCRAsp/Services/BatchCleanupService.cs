using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Services;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Services;

/// <summary>
/// Service for cleaning up expired (soft-deleted) batches across all orgs.
/// </summary>
public interface IBatchCleanupService
{
    /// <summary>
    /// Hard-deletes all soft-deleted batches in all orgs whose MarkedForDeletionAtUtc has passed the retention window.
    /// Returns total count of batches hard-deleted.
    /// </summary>
    Task<int> CleanupExpiredBatchesAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}

public class BatchCleanupService : IBatchCleanupService
{
    private readonly IPathService _paths;
    private readonly DatabaseSettings _settings;
    private readonly IBatchNotificationService _batchNotificationSvc;
    private readonly ILogger<BatchCleanupService> _logger;

    public BatchCleanupService(
        IPathService paths,
        DatabaseSettings settings,
        IBatchNotificationService batchNotificationSvc,
        ILogger<BatchCleanupService> logger)
    {
        _paths                = paths;
        _settings             = settings;
        _batchNotificationSvc = batchNotificationSvc;
        _logger               = logger;
    }

    public async Task<int> CleanupExpiredBatchesAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var totalHardDeleted = 0;

        // Find all org folders under BaseDirectory
        if (!Directory.Exists(_paths.BaseDirectory))
            return 0;

        var orgDirs = Directory.EnumerateDirectories(_paths.BaseDirectory);
        foreach (var orgDir in orgDirs)
        {
            var orgName = Path.GetFileName(orgDir);
            try
            {
                var dbPath = _paths.GetOrgDbPath(orgName);
                if (!File.Exists(dbPath))
                    continue;

                var optionsBuilder = new DbContextOptionsBuilder<KOCRDbContext>()
                    .UseSqlite($"Data Source={dbPath}");

                if (_settings.EnableSensitiveDataLogging)
                    optionsBuilder.EnableSensitiveDataLogging();
                if (_settings.EnableDetailedErrors)
                    optionsBuilder.EnableDetailedErrors();

                await using var context = new KOCRDbContext(optionsBuilder.Options);
                context.Database.Migrate();

                var cutoff = DateTime.UtcNow - retention;
                var expiredBatches = await context.Batches
                    .Where(b => b.MarkedForDeletionAtUtc != null && b.MarkedForDeletionAtUtc <= cutoff)
                    .ToListAsync(cancellationToken);

                var deletedBatchNames = new List<string>();

                foreach (var batch in expiredBatches)
                {
                    // Delete files from disk
                    var invoicesPath = Path.Combine(batch.FolderPath, "Invoices");
                    var artifactsPath = Path.Combine(batch.FolderPath, "Artifacts");
                    try
                    {
                        if (Directory.Exists(invoicesPath))
                            Directory.Delete(invoicesPath, recursive: true);
                        if (Directory.Exists(artifactsPath))
                            Directory.Delete(artifactsPath, recursive: true);
                        if (Directory.Exists(batch.FolderPath)
                            && !Directory.EnumerateFileSystemEntries(batch.FolderPath).Any())
                            Directory.Delete(batch.FolderPath, recursive: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete batch files for org {OrgName}, batch {BatchId} ({BatchName}) at {FolderPath}",
                            orgName, batch.BatchId, batch.Name, batch.FolderPath);
                    }

                    // Delete invoice rows (cascade will handle items/fields)
                    var invoiceRows = await context.Invoices
                        .Where(i => i.BatchId == batch.BatchId)
                        .ToListAsync(cancellationToken);
                    context.Invoices.RemoveRange(invoiceRows);

                    // Remove the batch row
                    context.Batches.Remove(batch);
                    deletedBatchNames.Add(batch.Name);
                    totalHardDeleted++;
                }

                await context.SaveChangesAsync(cancellationToken);

                foreach (var batchName in deletedBatchNames)
                {
                    try
                    {
                        await _batchNotificationSvc.NotifyBatchHardDeletedAsync(orgName, batchName, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send hard-delete notification for batch '{Batch}' in org '{OrgName}'",
                            batchName, orgName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired batches for org {OrgName}", orgName);
            }
        }

        return totalHardDeleted;
    }
}
