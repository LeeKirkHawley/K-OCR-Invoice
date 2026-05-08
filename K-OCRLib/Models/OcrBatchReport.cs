using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models;

/// <summary>Master-DB record of a batch OCR event (one row per batch run).</summary>
[Table("OcrBatchReports")]
public class OcrBatchReport
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string OrganizationName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BatchName { get; set; } = string.Empty;

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual ICollection<OcrBatchReportItem> Items { get; set; } = new List<OcrBatchReportItem>();
}

/// <summary>Per-invoice outcome row within an <see cref="OcrBatchReport"/>.</summary>
[Table("OcrBatchReportItems")]
public class OcrBatchReportItem
{
    [Key]
    public int Id { get; set; }

    public int OcrBatchReportId { get; set; }

    [Required, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    public bool OcrSucceeded { get; set; }

    [Required, MaxLength(100)]
    public string OcrService { get; set; } = string.Empty;

    /// <summary>Number of pages in the invoice document (used for per-page billing).</summary>
    public int PageCount { get; set; } = 1;

    public virtual OcrBatchReport? Report { get; set; }
}
