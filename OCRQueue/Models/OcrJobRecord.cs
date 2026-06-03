namespace OCRQueue.Models;

/// <summary>
/// Durable OCR job record stored in each org's SQLite database.
/// This is the queue-local contract used by OCRQueue and mapped to the EF entity internally.
/// </summary>
public sealed class OcrJobRecord
{
    public int Id { get; set; }
    public int? InvoiceId { get; set; }
    public int BatchId { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string OrgName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string WorkflowKey { get; set; } = "Default";
    public string Status { get; set; } = string.Empty;
    public DateTime QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
