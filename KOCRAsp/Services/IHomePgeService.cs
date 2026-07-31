using K_OCRLib.Models;
using KOCRAsp.Models;

namespace KOCRAsp.Services
{
    public interface IHomePageService
    {
        Task<IReadOnlyList<BatchSummary>> GetBatchesForOrgAsync(string orgId);
        Task<BatchSummary?> GetCurrentBatchAsync(string orgId, int? batchId);
        Task<(List<FileListEntry> Items, int Total, int Page, int TotalPages)> BuildPagedFileListAsync(
            BatchSummary batch, int page, int pageSize, bool isGuestOrganization);
        Task<UploadResult> UploadFilesAsync(
            int batchId,
            List<FileUpload> uploads,
            string userId,
            bool isGuestOrganization,
            string organizationName,
            string orgUser,
            string batchName);
        Task<(int Total, int Processed)> BuildOcrStatusAsync(BatchSummary batch, int batchId, int baseline);
        Task<IReadOnlyList<string>> GetAlreadyOcrdFilesAsync(int batchId);
        Task<bool> RequiresBatchValidationForExportAsync(string orgName);
        Task<BatchDetail?> GetBatchDetailAsync(int batchId);
        Task<InvoiceDto?> LoadInvoiceAsync(string filePath);
        Task SaveInvoiceAsync(string filePath, InvoiceDto invoice);
        Task AcceptValidationAsync(string filePath, string organizationName, string batchName, string orgUser);
        Task<FileListEntry?> BuildInvoiceDotStateAsync(int invoiceId);
        Task<List<(string FileName, InvoiceDto Invoice)>> LoadBatchInvoicesAsync(BatchSummary batch);
        Task RemoveInvoiceFromBatchAsync(string filePath, int sourceBatchId, string batchName, string orgUser);
        Task MoveInvoiceToBatchAsync(string filePath, int sourceBatchId, int targetBatchId, string orgUser);
    }
}
