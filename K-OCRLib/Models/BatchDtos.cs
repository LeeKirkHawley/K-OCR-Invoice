namespace K_OCRLib.Models;

/// <summary>Lightweight projection of a Batch — no large OCR blobs.</summary>
public class BatchSummary
{
    public int BatchId { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public string? LockedByUserId { get; set; }
    public DateTime? LockAcquiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime? MarkedForDeletionAtUtc { get; set; }

    public bool IsMarkedForDeletion => MarkedForDeletionAtUtc.HasValue;
}

public class BatchDetail : BatchSummary
{
    public string OrganizationName { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public int ValidatedCount { get; set; }
    public bool AllValidated => FileCount > 0 && ValidatedCount == FileCount;
}

public class CreateBatchRequest
{
    public string Name { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    /// <summary>Display name of the owning org (for folder path construction).</summary>
    public string OrgName { get; set; } = string.Empty;
    /// <summary>Maximum active batches for a guest org (sourced from settings at request time).</summary>
    public int GuestMaxBatches { get; init; } = 2;
}

public class CreateBatchResult
{
    public bool Success { get; set; }
    public int BatchId { get; set; }
    public string? ErrorMessage { get; set; }

    public static CreateBatchResult Ok(int batchId) => new() { Success = true, BatchId = batchId };
    public static CreateBatchResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

public class AcquireLockResult
{
    public bool Success { get; set; }
    public string? LockedByUserId { get; set; }
    public DateTime? LockAcquiredAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public static AcquireLockResult Ok() => new() { Success = true };
    public static AcquireLockResult Locked(string userId, DateTime lockedAt) =>
        new() { Success = false, LockedByUserId = userId, LockAcquiredAtUtc = lockedAt,
                ErrorMessage = $"Batch is locked by another user since {lockedAt:u}." };
}

public class UploadResult
{
    public bool Success { get; set; }
    public int FilesUploaded { get; set; }
    public int FilesSkippedByLimit { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<string> ConflictingFileNames { get; set; } = [];

    public static UploadResult Ok(int count, int skippedByLimit = 0) =>
        new() { Success = true, FilesUploaded = count, FilesSkippedByLimit = skippedByLimit };
    public static UploadResult ConflictError(IReadOnlyList<string> names) =>
        new() { Success = false, ConflictingFileNames = names,
                ErrorMessage = "One or more files already exist in this batch." };
    public static UploadResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>Framework-neutral file upload payload. Replaces IBrowserFile at the service boundary.</summary>
public sealed record FileUpload(string FileName, string ClientPath, Stream Content);

/// <summary>Result of a batch OCR trigger.</summary>
public class TriggerOcrResult
{
    /// <summary>True when the operation succeeded.</summary>
    public bool Success { get; init; } = true;
    
    /// <summary>Error message if the operation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when every file in the batch was already processed and nothing was OCR'd.</summary>
    public bool AllSkipped { get; init; }

    /// <summary>File paths that were successfully enqueued for OCR in this run.</summary>
    public IReadOnlyList<string> QueuedFilePaths { get; init; } = [];

    public static TriggerOcrResult Ok(IReadOnlyList<string>? queuedFilePaths = null) =>
        new() { Success = true, QueuedFilePaths = queuedFilePaths ?? [] };
    public static TriggerOcrResult Skipped() => new() { Success = true, AllSkipped = true };
    public static TriggerOcrResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
