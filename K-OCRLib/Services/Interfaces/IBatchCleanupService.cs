namespace K_OCRLib.Services.Interfaces;

public interface IBatchCleanupService
{
    /// <summary>
    /// Hard-deletes all soft-deleted batches across all orgs whose MarkedForDeletionAtUtc has
    /// passed the retention window. Returns the total count of batches hard-deleted.
    /// </summary>
    Task<int> CleanupExpiredBatchesAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
