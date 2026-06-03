using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// Durable storage for OCR job records in each org's SQLite database.
/// Does not use <see cref="K_OCR.Services.ITenantContext"/>; the caller supplies
/// <paramref name="orgName"/> so that the processor can act on behalf of any org.
/// </summary>
public interface IOcrQueueRepository
{
    /// <summary>Returns all Queued and Processing jobs for the given org, ordered by queue time.</summary>
    Task<IReadOnlyList<OcrJobRecord>> GetPendingJobsAsync(string orgName, CancellationToken ct = default);

    Task<OcrJobRecord> CreateJobAsync(OcrJob job, string orgName, CancellationToken ct = default);

    /// <summary>Resets an interrupted Processing job back to Queued so it can be re-enqueued on startup.</summary>
    Task MarkQueuedAsync(int jobId, string orgName, CancellationToken ct = default);

    Task MarkProcessingAsync(int jobId, string orgName, CancellationToken ct = default);
    Task MarkCompletedAsync(int jobId, string orgName, CancellationToken ct = default);
    Task MarkFailedAsync(int jobId, string orgName, string error, CancellationToken ct = default);
}
