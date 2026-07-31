using K_OCRLib.Models;

namespace KOCRAsp.Services
{
    public interface IHomeExportService
    {
        Task<byte[]> BuildDocxAsync(string filePath, InvoiceDto invoice);
        string BuildBatchJson(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices);
        byte[] BuildBatchExcel(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices);
        Task LogBatchExportAsync(BatchSummary batch, IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices, string orgUser);
        Task TrySoftDeleteBatchAfterExportAsync(BatchSummary batch, string userId, string orgId, string orgName, string orgUser);
    }
}
