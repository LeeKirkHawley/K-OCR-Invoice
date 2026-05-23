using K_OCR.Services;
using Microsoft.Extensions.Logging;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Durably persists OCR jobs via <see cref="IOcrQueueRepository"/> and then
/// pushes them into the in-memory <see cref="IOcrJobQueue"/> for processing.
/// </summary>
public sealed class OcrEnqueueService : IOcrEnqueueService
{
    private readonly IOcrQueueRepository _repository;
    private readonly IOcrJobQueue _queue;
    private readonly ILogger<OcrEnqueueService> _logger;

    public OcrEnqueueService(
        IOcrQueueRepository repository,
        IOcrJobQueue queue,
        ILogger<OcrEnqueueService> logger)
    {
        _repository = repository;
        _queue      = queue;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task<int> EnqueueFilesAsync(
        IReadOnlyList<(string FilePath, int InvoiceId)> files,
        int batchId,
        string orgId,
        string orgName,
        string workflowKey,
        CancellationToken ct = default)
    {
        var enqueued = 0;

        foreach (var (filePath, invoiceId) in files)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Persist durable job record first so it survives a process restart.
                var placeholder = new OcrJob(
                    JobId:       0,
                    InvoiceId:   invoiceId,
                    BatchId:     batchId,
                    OrgId:       orgId,
                    OrgName:     orgName,
                    FilePath:    filePath,
                    WorkflowKey: workflowKey,
                    QueuedAtUtc: now);

                var entity = await _repository.CreateJobAsync(placeholder, orgName, ct);

                // Build the enqueue token with the DB-assigned job ID.
                var job = new OcrJob(
                    JobId:       entity.Id,
                    InvoiceId:   invoiceId,
                    BatchId:     batchId,
                    OrgId:       orgId,
                    OrgName:     orgName,
                    FilePath:    filePath,
                    WorkflowKey: workflowKey,
                    QueuedAtUtc: now);

                await _queue.EnqueueAsync(job, ct);
                enqueued++;

                _logger.LogDebug(
                    "Enqueued OCR job {JobId} for invoice {InvoiceId} (batch {BatchId}, org {OrgName}, workflow {WorkflowKey}).",
                    entity.Id, invoiceId, batchId, orgName, workflowKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to enqueue OCR job for file '{FilePath}' (batch {BatchId}, org {OrgName}).",
                    filePath, batchId, orgName);
            }
        }

        _logger.LogInformation(
            "EnqueueFilesAsync: {Enqueued}/{Total} jobs enqueued for batch {BatchId} (org {OrgName}, workflow {WorkflowKey}).",
            enqueued, files.Count, batchId, orgName, workflowKey);

        return enqueued;
    }
}
