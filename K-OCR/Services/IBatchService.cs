using K_OCR.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace K_OCR.Services;

public interface IBatchService
{
    Task<int> GetNextBatchNumberAsync(string organizationId);
    Task<BatchSummary[]> GetBatchesForOrgAsync(string organizationId);
    Task<BatchSummary[]> GetAllBatchesAsync();
    Task<BatchDetail?> GetBatchDetailAsync(int batchId);
    Task<CreateBatchResult> CreateBatchAsync(CreateBatchRequest request);
    Task DeleteBatchAsync(int batchId, string requestingUserId);
    Task<AcquireLockResult> TryAcquireBatchLockAsync(int batchId, string userId);
    Task ReleaseBatchLockAsync(int batchId, string userId);
    Task SetUserLastBatchAsync(string userId, string organizationId, int? batchId);
    Task<int?> GetUserLastBatchIdAsync(string userId, string organizationId);
    Task TriggerOcrAsync(int batchId);
    Task<UploadResult> UploadFilesToBatchAsync(int batchId, IReadOnlyList<IBrowserFile> files, string userId);
}
