using Docnet.Core;
using Docnet.Core.Models;
using K_OCRLib.Data;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCRLib.Services;

public class BatchService : IBatchService, IAsyncDisposable
{
    private static readonly string[] InvoiceExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    // Prevents concurrent OCR runs on the same batch from the same process.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim>
        _ocrLocks = new();

    private readonly IDbContextFactory<KOCRDbContext> _dbFactory;
    private readonly IPathService _pathService;
    // TODO: Remove _processingService once the queue path is fully stable (kept to avoid breaking test mocks).
    private readonly IInvoiceProcessingService _processingService;
    private readonly IFileService _fileService;
    private readonly ITenantContext _tenantContext;
    private readonly IReportingService _reportingService;
    private readonly IStripeUsageService _stripeUsage;
    private readonly IOcrEnqueueService _ocrEnqueueSvc;
    private readonly ITrialOrganizationLimitService _trialLimitSvc;
    private readonly ILogger<BatchService> _logger;

    private KOCRDbContext? _db;
    private KOCRDbContext Db => _db ??= _dbFactory.CreateDbContext();

    public BatchService(
        IDbContextFactory<KOCRDbContext> dbFactory,
        IPathService pathService,
        IInvoiceProcessingService processingService,
        IFileService fileService,
        ITenantContext tenantContext,
        IReportingService reportingService,
        IStripeUsageService stripeUsage,
        IOcrEnqueueService ocrEnqueueSvc,
        ITrialOrganizationLimitService trialLimitSvc,
        ILogger<BatchService> logger)
    {
        _dbFactory          = dbFactory;
        _pathService        = pathService;
        _processingService  = processingService;
        _fileService        = fileService;
        _tenantContext      = tenantContext;
        _reportingService   = reportingService;
        _stripeUsage        = stripeUsage;
        _ocrEnqueueSvc      = ocrEnqueueSvc;
        _trialLimitSvc      = trialLimitSvc;
        _logger             = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    public async Task<int> GetNextBatchNumberAsync(string organizationId)
    {
        var max = await Db.Batches
            .MaxAsync(b => (int?)b.BatchNumber) ?? 0;
        return max + 1;
    }

    public async Task<BatchSummary[]> GetBatchesForOrgAsync(string organizationId)
    {
        return await Db.Batches
            .OrderBy(b => b.BatchNumber)
            .Select(b => new BatchSummary
            {
                BatchId                = b.BatchId,
                Name                   = b.Name,
                BatchNumber            = b.BatchNumber,
                FolderPath             = b.FolderPath,
                LockedByUserId         = b.LockedByUserId,
                LockAcquiredAtUtc      = b.LockAcquiredAtUtc,
                CreatedAtUtc           = b.CreatedAtUtc,
                CreatedByUserId        = b.CreatedByUserId,
                MarkedForDeletionAtUtc = b.MarkedForDeletionAtUtc,
            })
            .ToArrayAsync();
    }

    public async Task<BatchDetail?> GetBatchDetailAsync(int batchId)
    {
        var batch = await Db.Batches.FindAsync(batchId);
        if (batch is null) return null;

        var fileCount      = await Db.Invoices.CountAsync(i => i.BatchId == batchId);
        var validatedCount = await Db.Invoices.CountAsync(i => i.BatchId == batchId && i.IsInvoiceAccepted);

        return new BatchDetail
        {
            BatchId           = batch.BatchId,
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
        // Guest organizations are limited to 2 active batches.
        if (_tenantContext.IsGuestOrganization)
        {
            var guestMaxBatches = request.GuestMaxBatches;
            var activeBatchCount = await Db.Batches
                .CountAsync(b => b.MarkedForDeletionAtUtc == null);
            if (activeBatchCount >= guestMaxBatches)
                return CreateBatchResult.Error(
                    $"Guest organizations are limited to {guestMaxBatches} batches. " +
                    "Contact us to upgrade to a full account.");
        }

        // Uniqueness checks before opening a transaction
        var dupName = await Db.Batches
            .AnyAsync(b => b.Name == request.Name);
        if (dupName)
            return CreateBatchResult.Error($"A batch named '{request.Name}' already exists in this organization.");

        var sanitizedNewPath = _pathService.GetBatchFolderPath(request.OrgName, request.Name);
        var existingFolders = await Db.Batches
            .Select(b => b.FolderPath)
            .ToListAsync();
        if (existingFolders.Any(f => string.Equals(f, sanitizedNewPath, StringComparison.OrdinalIgnoreCase)))
            return CreateBatchResult.Error("A batch with the same sanitized folder name already exists.");

        var strategy = Db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                var batchNumber = await GetNextBatchNumberAsync(string.Empty);
                var folderPath  = sanitizedNewPath;

                var batch = new Batch
                {
                    Name            = request.Name,
                    BatchNumber     = batchNumber,
                    FolderPath      = folderPath,
                    CreatedByUserId = request.CreatedByUserId,
                    CreatedAtUtc    = DateTime.UtcNow,
                };

                Db.Batches.Add(batch);
                await Db.SaveChangesAsync();

                // Create folders on disk
                try
                {
                    Directory.CreateDirectory(Path.Combine(folderPath, "Invoices"));
                    Directory.CreateDirectory(Path.Combine(folderPath, "Artifacts"));
                }
                catch (Exception ex)
                {
                    Db.Batches.Remove(batch);
                    await Db.SaveChangesAsync();
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

    public async Task<string> DeleteBatchAsync(int batchId, string requestingUserId)
    {
        var batch = await Db.Batches.FindAsync(batchId)
                    ?? throw new InvalidOperationException($"Batch {batchId} not found.");

        if (batch.LockedByUserId is not null && batch.LockedByUserId != requestingUserId)
            throw new InvalidOperationException("Cannot delete a batch locked by another user.");

        var batchName = batch.Name;

        // Mark for deletion (soft delete) instead of hard delete
        batch.MarkedForDeletionAtUtc = DateTime.UtcNow;
        Db.Batches.Update(batch);
        await Db.SaveChangesAsync();

        return batchName;
    }

    /// <summary>
    /// Hard-deletes all batches whose MarkedForDeletionAtUtc has passed the retention window.
    /// Returns count of batches hard-deleted.
    /// </summary>
    public async Task<int> CleanupExpiredBatchesAsync(TimeSpan retention)
    {
        var cutoff = DateTime.UtcNow - retention;
        var expiredBatches = await Db.Batches
            .Where(b => b.MarkedForDeletionAtUtc != null && b.MarkedForDeletionAtUtc <= cutoff)
            .ToListAsync();

        foreach (var batch in expiredBatches)
        {
            // Delete files from disk
            var invoicesPath = Path.Combine(batch.FolderPath, "Invoices");
            var artifactsPath = Path.Combine(batch.FolderPath, "Artifacts");
            try
            {
                if (Directory.Exists(invoicesPath))
                    Directory.Delete(invoicesPath, recursive: true);
                if (Directory.Exists(artifactsPath))
                    Directory.Delete(artifactsPath, recursive: true);
                if (Directory.Exists(batch.FolderPath)
                    && !Directory.EnumerateFileSystemEntries(batch.FolderPath).Any())
                    Directory.Delete(batch.FolderPath, recursive: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete batch files for {BatchId} ({BatchName}) at {FolderPath}",
                    batch.BatchId, batch.Name, batch.FolderPath);
            }

            // Delete invoice rows (cascade will handle items/fields)
            var invoiceRows = await Db.Invoices.Where(i => i.BatchId == batch.BatchId).ToListAsync();
            Db.Invoices.RemoveRange(invoiceRows);

            // Remove the batch row (UserBatchSession.BatchId SET NULL by FK)
            Db.Batches.Remove(batch);
        }

        await Db.SaveChangesAsync();
        return expiredBatches.Count;
    }

    public async Task<AcquireLockResult> TryAcquireBatchLockAsync(int batchId, string userId)
    {
        var strategy = Db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            var batch = await Db.Batches.FindAsync(batchId);
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
            await Db.SaveChangesAsync();
            await tx.CommitAsync();
            return AcquireLockResult.Ok();
        });
    }

    public async Task ReleaseBatchLockAsync(int batchId, string userId)
    {
        var batch = await Db.Batches.FindAsync(batchId);
        if (batch is null) return;
        if (batch.LockedByUserId == userId)
        {
            batch.LockedByUserId    = null;
            batch.LockAcquiredAtUtc = null;
            await Db.SaveChangesAsync();
        }
    }

    public async Task SetUserLastBatchAsync(string userId, string organizationId, int? batchId)
    {
        var session = await Db.UserBatchSessions
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (session is null)
        {
            session = new UserBatchSession
            {
                UserId            = userId,
                BatchId           = batchId,
                LastAccessedAtUtc = DateTime.UtcNow,
            };
            Db.UserBatchSessions.Add(session);
        }
        else
        {
            session.BatchId           = batchId;
            session.LastAccessedAtUtc = DateTime.UtcNow;
        }

        await Db.SaveChangesAsync();
    }

    public async Task<int?> GetUserLastBatchIdAsync(string userId, string organizationId)
    {
        var session = await Db.UserBatchSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId);
        return session?.BatchId;
    }

    public async Task<List<string>> GetFullyProcessedFileNamesAsync(int batchId)
    {
        return await Db.Invoices
            .Where(i => i.BatchId == batchId && i.IsFullyProcessed && i.FilePath != null)
            .Select(i => Path.GetFileName(i.FilePath!))
            .ToListAsync();
    }

    private static int EstimatePdfPageCount(string filePath)
    {
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(72, 72));
            return docReader.GetPageCount();
        }
        catch
        {
            return 1;
        }
    }

    private static int EstimateTiffPageCount(string filePath)
    {
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(filePath);
            return codec is not null && codec.FrameCount > 0 ? codec.FrameCount : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static int EstimateFilePageCount(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return EstimatePdfPageCount(filePath);
        if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return EstimateTiffPageCount(filePath);
        return 1;
    }

    public async Task<TriggerOcrResult> TriggerOcrAsync(int batchId, double? minConfidenceThreshold = null, ISet<string>? skipFileNames = null, string? workflowKey = null, int? maxPageCount = null, int? maxInvoicesPerBatch = null)
    {
        if (!_tenantContext.IsTrialOrganization &&
            !_stripeUsage.IsStatusActive(_tenantContext.StripeSubscriptionStatus))
            throw new InvalidOperationException("OCR is unavailable: subscription inactive.");

        // Block OCR if guest org exceeds batch limit
        var limitStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        if (limitStatus.IsGuestOrganization && limitStatus.IsBatchLimitExceeded())
            return TriggerOcrResult.Error(
                $"This guest organization has reached its batch limit ({limitStatus.UsedBatches}/{limitStatus.MaxBatches} batches). " +
                $"Please delete or export an existing batch to create new ones.");

        var batchLock = _ocrLocks.GetOrAdd(batchId, _ => new SemaphoreSlim(1, 1));
        if (!await batchLock.WaitAsync(0))
        {
            _logger.LogWarning("TriggerOcrAsync: batch {BatchId} is already being processed; ignoring duplicate request.", batchId);
            return TriggerOcrResult.Ok();
        }

        // Unique key for this OCR run — used as Stripe idempotency key so a duplicate
        // concurrent or retried request cannot double-charge the customer.
        var ocrRunId = Guid.NewGuid().ToString("N");

        try
        {
            var batch = await Db.Batches.FindAsync(batchId);
            if (batch is null)
            {
                _logger.LogWarning("TriggerOcrAsync: batch {BatchId} not found.", batchId);
                return TriggerOcrResult.Ok();
            }

            var invoicesDir  = Path.Combine(batch.FolderPath, "Invoices");
            var artifactsDir = Path.Combine(batch.FolderPath, "Artifacts");

            if (!Directory.Exists(invoicesDir))
            {
                _logger.LogWarning("TriggerOcrAsync: invoices folder does not exist for batch {BatchId}.", batchId);
                return TriggerOcrResult.Ok();
            }

            var filePaths = _fileService.LoadFiles(invoicesDir, InvoiceExtensions).ToList();
            if (filePaths.Count == 0)
            {
                _logger.LogInformation("TriggerOcrAsync: no files to process in batch {BatchId}.", batchId);
                return TriggerOcrResult.Ok();
            }

            // ── Apply caller-requested skip list ─────────────────────────
            if (skipFileNames != null && skipFileNames.Count > 0)
            {
                filePaths = filePaths
                    .Where(f => !skipFileNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    .ToList();
                _logger.LogInformation("TriggerOcrAsync: {Skipped} file(s) skipped per caller request; {Remaining} will be OCR'd (batch {BatchId}).",
                    skipFileNames.Count, filePaths.Count, batchId);
            }

            if (filePaths.Count == 0)
            {
                _logger.LogInformation("TriggerOcrAsync: no files remaining to process in batch {BatchId}.", batchId);
                return TriggerOcrResult.Skipped();
            }

            _logger.LogInformation("TriggerOcrAsync: processing {Count} file(s) in batch {BatchId}.", filePaths.Count, batchId);

            var orgId   = _tenantContext.OrganizationId   ?? string.Empty;
            var orgName = _tenantContext.OrganizationName ?? string.Empty;

            // Look up Invoice.Id and TotalPages for each file path.
            var invoiceDataByPath = await Db.Invoices
                .Where(i => i.BatchId == batchId && i.FilePath != null)
                .ToDictionaryAsync(i => i.FilePath!, i => new { i.Id, i.TotalPages });

            // Filter out invoices that exceed the per-invoice page limit.
            if (maxPageCount.HasValue)
            {
                var overLimit = filePaths
                    .Where(fp => invoiceDataByPath.TryGetValue(fp, out var d) && d.TotalPages > maxPageCount.Value)
                    .ToList();
                if (overLimit.Count > 0)
                {
                    _logger.LogInformation(
                        "TriggerOcrAsync: {Count} file(s) exceed the max page count ({Max}) and will be skipped (batch {BatchId}).",
                        overLimit.Count, maxPageCount.Value, batchId);
                    filePaths = filePaths.Except(overLimit).ToList();
                }
            }

            var invoiceIdByPath = invoiceDataByPath.ToDictionary(kv => kv.Key, kv => kv.Value.Id);

            // Cap the number of invoices to OCR per batch.
            if (maxInvoicesPerBatch.HasValue && filePaths.Count > maxInvoicesPerBatch.Value)
            {
                _logger.LogInformation(
                    "TriggerOcrAsync: capping {Total} file(s) to {Max} per maxInvoicesPerBatch limit (batch {BatchId}).",
                    filePaths.Count, maxInvoicesPerBatch.Value, batchId);
                filePaths = filePaths.Take(maxInvoicesPerBatch.Value).ToList();
            }

            var jobItems = filePaths
                .Where(fp => invoiceIdByPath.ContainsKey(fp))
                .Select(fp => (fp, invoiceIdByPath[fp]))
                .ToList();

            if (jobItems.Count < filePaths.Count)
                _logger.LogWarning(
                    "TriggerOcrAsync: {Missing} file(s) had no Invoice record and will be skipped (batch {BatchId}).",
                    filePaths.Count - jobItems.Count, batchId);

            await _ocrEnqueueSvc.EnqueueFilesAsync(
                jobItems, batchId, orgId, orgName, workflowKey ?? "Default");

            // TODO: RecordBatchOcrEventAsync requires actual OCR results; move to OcrQueueProcessor completion callback.

            // Bill Stripe now with estimated page counts; actual page counts are available after OCR completes.
            if (!_tenantContext.IsTrialOrganization && _tenantContext.StripeCustomerId is { } customerId)
            {
                var estimatedPages = filePaths.Sum(EstimateFilePageCount);
                var idempotencyKey = $"batch-{batchId}-{ocrRunId}";
                await _stripeUsage.ReportUsageAsync(customerId, estimatedPages, idempotencyKey);
            }

            var enqueuedPaths = jobItems.Select(j => j.Item1).ToList();
            return TriggerOcrResult.Ok(enqueuedPaths);
        }
        finally
        {
            batchLock.Release();
            // Clean up the lock entry once released so the dictionary doesn't grow unbounded.
            _ocrLocks.TryRemove(batchId, out _);
        }
    }

    public async Task<UploadResult> UploadFilesToBatchAsync(
        int batchId,
        IReadOnlyList<FileUpload> files,
        string userId,
        int? maxInvoicesPerBatch = null)
    {
        var batch = await Db.Batches.FindAsync(batchId);
        if (batch is null)
            return UploadResult.Error($"Batch {batchId} not found.");

        var lockResult = await TryAcquireBatchLockAsync(batchId, userId);
        if (!lockResult.Success)
            return UploadResult.Error(lockResult.ErrorMessage ?? "Batch is locked by another user.");

        try
        {
            var strategy = Db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await Db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
                try
                {
                    // Check for filename conflicts in this batch
                    var incomingNames  = files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
                    var existing = await Db.Invoices
                        .Where(i => i.BatchId == batchId && i.FilePath != null)
                        .Select(i => Path.GetFileName(i.FilePath!))
                        .ToListAsync();

                    // Enforce per-batch invoice limit.
                    int skippedByLimit = 0;
                    IReadOnlyList<FileUpload> filesToUpload = files;
                    if (maxInvoicesPerBatch.HasValue)
                    {
                        int slotsRemaining = maxInvoicesPerBatch.Value - existing.Count;
                        if (slotsRemaining <= 0)
                            return UploadResult.Error(
                                $"This batch is at the maximum of {maxInvoicesPerBatch.Value} invoice(s). Create a new batch to continue.");
                        if (files.Count > slotsRemaining)
                        {
                            skippedByLimit = files.Count - slotsRemaining;
                            filesToUpload = files.Take(slotsRemaining).ToList();
                        }
                    }

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
                    var uploadedFiles = new List<(string ClientFileName, string ClientPath, string ServerFileName, string ServerPath)>();
                    foreach (var file in filesToUpload)
                    {
                        var destPath = Path.Combine(invoicesFolder, file.FileName);
                        await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write);
                        await file.Content.CopyToAsync(dest);

                        Db.Invoices.Add(new Invoice
                        {
                            BatchId       = batchId,
                            FilePath      = destPath,
                            UploadedAtUtc = DateTime.UtcNow,
                            TotalPages    = EstimateFilePageCount(destPath),
                        });
                        uploadedFiles.Add((
                            file.FileName,
                            file.ClientPath,
                            Path.GetFileName(destPath),
                            destPath));
                        uploaded++;
                    }

                    await Db.SaveChangesAsync();
                    await tx.CommitAsync();

                    foreach (var uploadedFile in uploadedFiles)
                    {
                        _logger.LogInformation(
                            "Invoice uploaded: BatchId={BatchId}, UserId={UserId}, ClientFileName={ClientFileName}, ClientPath={ClientPath}, ServerFileName={ServerFileName}, ServerPath={ServerPath}.",
                            batchId,
                            userId,
                            uploadedFile.ClientFileName,
                            uploadedFile.ClientPath,
                            uploadedFile.ServerFileName,
                            uploadedFile.ServerPath);
                    }

                    return UploadResult.Ok(uploaded, skippedByLimit);
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
