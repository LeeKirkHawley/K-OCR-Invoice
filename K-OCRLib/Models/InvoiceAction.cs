using System.ComponentModel.DataAnnotations;

namespace K_OCRLib.Models;

public class InvoiceAction
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    [Required, MaxLength(200)]
    public string BatchName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string InvoiceName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string OrgUser { get; set; } = string.Empty;

    /// <summary>Pages OCR'd — populated for OCRed events; 0 for all others.</summary>
    public int PageCount { get; set; }
}
