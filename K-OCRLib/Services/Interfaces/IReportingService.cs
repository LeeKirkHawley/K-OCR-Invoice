using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

public interface IReportingService
{
    /// <summary>
    /// Records a batch OCR event (organization, batch, and per-invoice outcomes)
    /// to the long-term reporting store.
    /// </summary>
    Task RecordBatchOcrEventAsync(BatchOcrReportRequest request);
}
