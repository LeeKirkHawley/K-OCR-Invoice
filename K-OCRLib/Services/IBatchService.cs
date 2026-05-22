using K_OCR.Models;

namespace K_OCR.Services;

public interface IBatchService
{
    Task<int> GetNextBatchNumberAsync(string organizationId);
    Task<BatchSummary[]> GetBatchesForOrgAsync(string organizationId);
    Task<BatchDetail?> GetBatchDetailAsync(int batchId);
    Task<CreateBatchResult> CreateBatchAsync(CreateBatchRequest request);
    Task<string> DeleteBatchAsync(int batchId, string requestingUserId);
    Task<int> CleanupExpiredBatchesAsync(TimeSpan retention);
    Task<AcquireLockResult> TryAcquireBatchLockAsync(int batchId, string userId);
    Task ReleaseBatchLockAsync(int batchId, string userId);
    Task SetUserLastBatchAsync(string userId, string organizationId, int? batchId);
    Task<int?> GetUserLastBatchIdAsync(string userId, string organizationId);
    Task<TriggerOcrResult> TriggerOcrAsync(int batchId, double? minConfidenceThreshold = null, int? guestPageLimit = null, ISet<string>? skipFileNames = null);
    Task<List<string>> GetFullyProcessedFileNamesAsync(int batchId);
    Task<int> GetBatchOcrdPageCountAsync(int batchId);
    Task<UploadResult> UploadFilesToBatchAsync(int batchId, IReadOnlyList<FileUpload> files, string userId);
}
