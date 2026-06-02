namespace K_OCRLib.Models;

/// <summary>Describes the OCR outcome for a single invoice file within a batch.</summary>
public class OcrInvoiceResult
{
    /// <summary>File name of the invoice (not the full path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Whether OCR completed successfully for this invoice.</summary>
    public bool OcrSucceeded { get; set; }

    /// <summary>The OCR service that processed the invoice (e.g. "Azure", "Tesseract").</summary>
    public string OcrService { get; set; } = string.Empty;

    /// <summary>Number of pages in the invoice document (used for per-page billing).</summary>
    public int PageCount { get; set; } = 1;
}

/// <summary>Payload for recording a batch OCR event in the reporting store.</summary>
public class BatchOcrReportRequest
{
    public string OrganizationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public IReadOnlyList<OcrInvoiceResult> Invoices { get; set; } = [];
}
