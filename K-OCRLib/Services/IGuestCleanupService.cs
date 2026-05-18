namespace K_OCR.Services;

public interface IGuestCleanupService
{
    /// <summary>
    /// Runs one full cleanup cycle: marks expired guest accounts for deletion, hard-deletes
    /// expired soft-deleted orgs, and hard-deletes expired soft-deleted batches.
    /// Returns the total number of items processed.
    /// </summary>
    Task<int> RunCleanupCycleAsync(CancellationToken cancellationToken = default);
}
