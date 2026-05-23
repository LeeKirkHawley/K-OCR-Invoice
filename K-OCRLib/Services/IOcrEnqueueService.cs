namespace K_OCR.Services;

/// <summary>
/// Durably persists OCR jobs and enqueues them for background processing.
/// Implemented in the OCRQueue library; registered as a singleton in the host.
/// </summary>
public interface IOcrEnqueueService
{
    /// <summary>
    /// Creates a durable <c>OcrJobEntity</c> record for each file and pushes it
    /// into the in-memory queue for processing by <c>OcrQueueProcessor</c>.
    /// </summary>
    /// <param name="files">
    /// Pairs of (absolute file path, existing Invoice.Id) for every invoice to process.
    /// The invoice record must already exist in the org's SQLite database.
    /// </param>
    /// <param name="batchId">The batch these files belong to.</param>
    /// <param name="orgId">The ASP.NET Identity organisation ID (GUID string).</param>
    /// <param name="orgName">The organisation's display name (used to locate its SQLite DB).</param>
    /// <param name="workflowKey">
    /// The workflow to run (e.g. "Default"). Resolved via <c>OcrWorkflowRegistry</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of jobs successfully enqueued.</returns>
    Task<int> EnqueueFilesAsync(
        IReadOnlyList<(string FilePath, int InvoiceId)> files,
        int batchId,
        string orgId,
        string orgName,
        string workflowKey,
        CancellationToken ct = default);
}
