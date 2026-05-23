namespace OCRQueue.Models;

/// <summary>
/// Immutable token representing a single OCR job. Instances are written into the
/// in-memory <see cref="System.Threading.Channels.Channel{T}"/> and passed through
/// the processor pipeline.
/// </summary>
public record OcrJob(
    int JobId,
    int InvoiceId,
    int BatchId,
    string OrgId,
    string OrgName,
    string FilePath,
    string WorkflowKey,
    DateTime QueuedAtUtc);
