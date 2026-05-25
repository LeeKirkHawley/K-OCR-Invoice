using K_OCR.Models;

namespace KOCRAsp.Models;

public class HomeIndexViewModel
{
    public string OrgId { get; set; } = string.Empty;
    public List<BatchSummary> AvailableBatches { get; set; } = new();
    public BatchSummary? CurrentBatch { get; set; }
    public List<FileListEntry> Files { get; set; } = new();
    public int TotalFiles { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages { get; set; }
    public int MaxPagesPerInvoice { get; set; } = 20;
}
