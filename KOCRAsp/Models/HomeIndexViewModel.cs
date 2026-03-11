using K_OCR.Models;

namespace KOCRAsp.Models;

public class HomeIndexViewModel
{
    public List<BatchSummary> AvailableBatches { get; set; } = new();
    public BatchSummary? CurrentBatch { get; set; }
    public List<FileListEntry> Files { get; set; } = new();
}
