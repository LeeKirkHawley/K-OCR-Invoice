namespace K_OCR.Models;

/// <summary>
/// Durable record of an OCR job stored in the org's per-org SQLite database.
/// Created when an invoice is enqueued; updated as the job progresses through
/// Queued → Processing → Completed/Failed.
/// </summary>
public class OcrJobEntity
{
    public int Id { get; set; }
    public int? InvoiceId { get; set; }
    public int BatchId { get; set; }

    /// <summary>
    /// The ASP.NET Identity organisation ID. Stored here so that a SuperAdmin
    /// can aggregate queue counts across orgs without opening every SQLite file.
    /// </summary>
    public string OrgId { get; set; } = string.Empty;

    public string OrgName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Matches <c>OrgConfig.OcrWorkflowKey</c>; defaults to "Default".</summary>
    public string WorkflowKey { get; set; } = "Default";

    /// <summary>Queued | Processing | Completed | Failed</summary>
    public string Status { get; set; } = OcrJobStatus.Queued;

    public DateTime QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public Invoice? Invoice { get; set; }
}

public static class OcrJobStatus
{
    public const string Queued     = "Queued";
    public const string Processing = "Processing";
    public const string Completed  = "Completed";
    public const string Failed     = "Failed";
}
