namespace K_OCR.Models;

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
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<string> ConflictingFileNames { get; set; } = [];

    public static UploadResult Ok(int count) => new() { Success = true, FilesUploaded = count };
    public static UploadResult ConflictError(IReadOnlyList<string> names) =>
        new() { Success = false, ConflictingFileNames = names,
                ErrorMessage = "One or more files already exist in this batch." };
    public static UploadResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>Framework-neutral file upload payload. Replaces IBrowserFile at the service boundary.</summary>
public sealed record FileUpload(string FileName, string ClientPath, Stream Content);
