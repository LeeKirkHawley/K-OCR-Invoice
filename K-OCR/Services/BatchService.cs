using K_OCR.Data;
using K_OCR.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public class BatchService : IBatchService
{
    private readonly KOCRDbContext _db;
    private readonly IPathService _pathService;
    private readonly ILogger<BatchService> _logger;

    public BatchService(
        KOCRDbContext db,
        IPathService pathService,
        ILogger<BatchService> logger)
    {
        _db = db;
        _pathService = pathService;
        _logger = logger;
    }

    public async Task<int> GetNextBatchNumberAsync(string organizationId)
    {
        var max = await _db.Batches
            .Where(b => b.OrganizationId == organizationId)
            .MaxAsync(b => (int?)b.BatchNumber) ?? 0;
        return max + 1;
    }

    public async Task<BatchSummary[]> GetBatchesForOrgAsync(string organizationId)
    {
        return await _db.Batches
            .Where(b => b.OrganizationId == organizationId)
            .OrderBy(b => b.BatchNumber)
            .Select(b => new BatchSummary
            {
                BatchId           = b.BatchId,
                OrganizationId    = b.OrganizationId,
                Name              = b.Name,
                BatchNumber       = b.BatchNumber,
                FolderPath        = b.FolderPath,
                LockedByUserId    = b.LockedByUserId,
                LockAcquiredAtUtc = b.LockAcquiredAtUtc,
                CreatedAtUtc      = b.CreatedAtUtc,
                CreatedByUserId   = b.CreatedByUserId,
            })
            .ToArrayAsync();
    }

    public async Task<BatchSummary[]> GetAllBatchesAsync()
    {
        return await _db.Batches
            .OrderBy(b => b.OrganizationId)
            .ThenBy(b => b.BatchNumber)
            .Select(b => new BatchSummary
            {
                BatchId           = b.BatchId,
                OrganizationId    = b.OrganizationId,
                Name              = b.Name,
                BatchNumber       = b.BatchNumber,
                FolderPath        = b.FolderPath,
                LockedByUserId    = b.LockedByUserId,
                LockAcquiredAtUtc = b.LockAcquiredAtUtc,
                CreatedAtUtc      = b.CreatedAtUtc,
                CreatedByUserId   = b.CreatedByUserId,
            })
            .ToArrayAsync();
    }

    public async Task<BatchDetail?> GetBatchDetailAsync(int batchId)
    {
        var batch = await _db.Batches.FindAsync(batchId);
        if (batch is null) return null;

        var fileCount      = await _db.Invoices.CountAsync(i => i.BatchId == batchId);
        var validatedCount = await _db.Invoices.CountAsync(i => i.BatchId == batchId && i.IsValidationAccepted);

        return new BatchDetail
        {
            BatchId           = batch.BatchId,
            OrganizationId    = batch.OrganizationId,
            Name              = batch.Name,
            BatchNumber       = batch.BatchNumber,
            FolderPath        = batch.FolderPath,
            LockedByUserId    = batch.LockedByUserId,
            LockAcquiredAtUtc = batch.LockAcquiredAtUtc,
            CreatedAtUtc      = batch.CreatedAtUtc,
            CreatedByUserId   = batch.CreatedByUserId,
            FileCount         = fileCount,
            ValidatedCount    = validatedCount,
        };
    }

    public async Task<CreateBatchResult> CreateBatchAsync(CreateBatchRequest request)
    {
        // Uniqueness checks before opening a transaction
        var dupName = await _db.Batches
            .AnyAsync(b => b.OrganizationId == request.OrganizationId
                        && b.Name == request.Name);
        if (dupName)
            return CreateBatchResult.Error($"A batch named '{request.Name}' already exists in this organization.");

        var sanitizedNewName = _pathService.SanitizeName(request.Name);
        var existingFolders = await _db.Batches
            .Where(b => b.OrganizationId == request.OrganizationId)
            .Select(b => b.FolderPath)
            .ToListAsync();
        var sanitizedNewPath = _pathService.GetBatchFolderPath(request.OrgName, request.Name);
        if (existingFolders.Any(f => string.Equals(f, sanitizedNewPath, StringComparison.OrdinalIgnoreCase)))
            return CreateBatchResult.Error("A batch with the same sanitized folder name already exists.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                var batchNumber = await GetNextBatchNumberAsync(request.OrganizationId);
                var folderPath  = sanitizedNewPath;

                var batch = new Batch
                {
                    OrganizationId  = request.OrganizationId,
                    Name            = request.Name,
                    BatchNumber     = batchNumber,
                    FolderPath      = folderPath,
                    CreatedByUserId = request.CreatedByUserId,
                    CreatedAtUtc    = DateTime.UtcNow,
                };

                _db.Batches.Add(batch);
                await _db.SaveChangesAsync();

                // Create folders on disk
                try
                {
                    Directory.CreateDirectory(Path.Combine(folderPath, "Invoices"));
                    Directory.CreateDirectory(Path.Combine(folderPath, "Artifacts"));
                }
                catch (Exception ex)
                {
                    _db.Batches.Remove(batch);
                    await _db.SaveChangesAsync();
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to create batch folders for {FolderPath}", folderPath);
                    return CreateBatchResult.Error($"Could not create batch folders: {ex.Message}");
                }

                await tx.CommitAsync();
                return CreateBatchResult.Ok(batch.BatchId);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error creating batch");
                return CreateBatchResult.Error($"Unexpected error: {ex.Message}");
            }
        });
    }

    public async Task DeleteBatchAsync(int batchId, string requestingUserId)
    {
        var batch = await _db.Batches.FindAsync(batchId)
                    ?? throw new InvalidOperationException($"Batch {batchId} not found.");

        if (batch.LockedByUserId is not null && batch.LockedByUserId != requestingUserId)
            throw new InvalidOperationException("Cannot delete a batch locked by another user.");

        // Delete files from disk
        var invoicesPath  = Path.Combine(batch.FolderPath, "Invoices");
        var artifactsPath = Path.Combine(batch.FolderPath, "Artifacts");
        if (Directory.Exists(invoicesPath))
            Directory.Delete(invoicesPath, recursive: true);
        if (Directory.Exists(artifactsPath))
            Directory.Delete(artifactsPath, recursive: true);
        if (Directory.Exists(batch.FolderPath)
            && !Directory.EnumerateFileSystemEntries(batch.FolderPath).Any())
            Directory.Delete(batch.FolderPath, recursive: false);

        // Delete invoice rows (cascade will handle items/fields)
        var invoiceRows = await _db.Invoices.Where(i => i.BatchId == batchId).ToListAsync();
        _db.Invoices.RemoveRange(invoiceRows);

        // Remove the batch row (UserBatchSession.BatchId SET NULL by FK)
        _db.Batches.Remove(batch);

        await _db.SaveChangesAsync();
    }

    public async Task<AcquireLockResult> TryAcquireBatchLockAsync(int batchId, string userId)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            var batch = await _db.Batches.FindAsync(batchId);
            if (batch is null)
            {
                await tx.RollbackAsync();
                return AcquireLockResult.Locked(string.Empty, DateTime.UtcNow);
            }

            var lockStaleThreshold = DateTime.UtcNow.AddMinutes(-5);
            if (batch.LockedByUserId is not null
                && batch.LockedByUserId != userId
                && batch.LockAcquiredAtUtc > lockStaleThreshold)
            {
                await tx.RollbackAsync();
                return AcquireLockResult.Locked(batch.LockedByUserId, batch.LockAcquiredAtUtc!.Value);
            }

            batch.LockedByUserId    = userId;
            batch.LockAcquiredAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return AcquireLockResult.Ok();
        });
    }

    public async Task ReleaseBatchLockAsync(int batchId, string userId)
    {
        var batch = await _db.Batches.FindAsync(batchId);
        if (batch is null) return;
        if (batch.LockedByUserId == userId)
        {
            batch.LockedByUserId    = null;
            batch.LockAcquiredAtUtc = null;
            await _db.SaveChangesAsync();
        }
    }

    public async Task SetUserLastBatchAsync(string userId, string organizationId, int? batchId)
    {
        var session = await _db.UserBatchSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.OrganizationId == organizationId);

        if (session is null)
        {
            session = new UserBatchSession
            {
                UserId           = userId,
                OrganizationId   = organizationId,
                BatchId          = batchId,
                LastAccessedAtUtc = DateTime.UtcNow,
            };
            _db.UserBatchSessions.Add(session);
        }
        else
        {
            session.BatchId           = batchId;
            session.LastAccessedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<int?> GetUserLastBatchIdAsync(string userId, string organizationId)
    {
        var session = await _db.UserBatchSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.OrganizationId == organizationId);
        return session?.BatchId;
    }

    public async Task TriggerOcrAsync(int batchId)
    {
        // Queues unprocessed invoices — actual processing is handled by InvoiceProcessingService.
        // For now, this is a stub that logs intent; Phase 6 wires up the full flow.
        _logger.LogInformation("TriggerOcrAsync called for batch {BatchId}. Full processing wired in Phase 6.", batchId);
        await Task.CompletedTask;
    }

    public async Task<UploadResult> UploadFilesToBatchAsync(
        int batchId,
        IReadOnlyList<IBrowserFile> files,
        string userId)
    {
        var batch = await _db.Batches.FindAsync(batchId);
        if (batch is null)
            return UploadResult.Error($"Batch {batchId} not found.");

        var lockResult = await TryAcquireBatchLockAsync(batchId, userId);
        if (!lockResult.Success)
            return UploadResult.Error(lockResult.ErrorMessage ?? "Batch is locked by another user.");

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
                try
                {
                    // Check for filename conflicts in this batch
                    var incomingNames = files.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
                    var existing = await _db.Invoices
                        .Where(i => i.BatchId == batchId && i.FilePath != null)
                        .Select(i => Path.GetFileName(i.FilePath!))
                        .ToListAsync();
                    var conflicts = existing
                        .Where(n => incomingNames.Contains(n ?? string.Empty))
                        .ToList();

                    if (conflicts.Count > 0)
                    {
                        await tx.RollbackAsync();
                        return UploadResult.ConflictError(conflicts);
                    }

                    Directory.CreateDirectory(invoicesFolder);

                    var uploaded = 0;
                    const long maxFileSize = 50 * 1024 * 1024; // 50 MB per file
                    foreach (var file in files)
                    {
                        var destPath = Path.Combine(invoicesFolder, file.Name);
                        await using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                        await using (var src  = file.OpenReadStream(maxFileSize))
                        {
                            await src.CopyToAsync(dest);
                        }

                        _db.Invoices.Add(new Invoice
                        {
                            BatchId       = batchId,
                            FilePath      = destPath,
                            UploadedAtUtc = DateTime.UtcNow,
                        });
                        uploaded++;
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return UploadResult.Ok(uploaded);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Error uploading files to batch {BatchId}", batchId);
                    return UploadResult.Error($"Upload failed: {ex.Message}");
                }
            });
        }
        finally
        {
            await ReleaseBatchLockAsync(batchId, userId);
        }
    }
}
