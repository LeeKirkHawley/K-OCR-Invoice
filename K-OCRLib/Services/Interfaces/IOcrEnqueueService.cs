namespace K_OCRLib.Services.Interfaces;

/// <summary>
/// Durably persists OCR jobs and enqueues them for background processing.
/// Implemented by OCRQueue and consumed by the library and web host.
/// </summary>
public interface IOcrEnqueueService
{
    /// <summary>
    /// Creates a durable OCR queue record for each file and pushes it into the in-memory queue
    /// for processing by the OCRQueue background processor.
    /// </summary>
    /// <param name="files">
    /// Pairs of (absolute file path, existing Invoice.Id) for every invoice to process.
    /// The invoice record must already exist in the org's SQLite database.
    /// </param>
    /// <param name="batchId">The batch these files belong to.</param>
    /// <param name="orgId">The ASP.NET Identity organisation ID (GUID string).</param>
    /// <param name="orgName">The organisation's display name (used to locate its SQLite DB).</param>
    /// <param name="workflowKey">
    /// The workflow to run (e.g. "Default"). Resolved via the OCRQueue workflow registry.
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
