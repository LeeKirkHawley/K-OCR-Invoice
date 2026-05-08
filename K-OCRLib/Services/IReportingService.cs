using K_OCR.Models;

namespace K_OCR.Services;

public interface IReportingService
{
    /// <summary>
    /// Records a batch OCR event (organization, batch, and per-invoice outcomes)
    /// to the long-term reporting store.
    /// </summary>
    Task RecordBatchOcrEventAsync(BatchOcrReportRequest request);
}
