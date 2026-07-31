using K_OCRLib.Models;

namespace KOCRAsp.Services
{
    public interface IHomeOcrService
    {
        bool CanUseOcr(HomeTenantInfo tenant);
        Task<HomeSingleOcrResult> StartOcrAsync(string filePath, HomeTenantInfo tenant, string orgUser, BatchSummary? batch);
        Task<HomeBatchOcrResult> BatchOcrAsync(int batchId, bool skipAlreadyOcrd, HomeTenantInfo tenant);
    }
}
