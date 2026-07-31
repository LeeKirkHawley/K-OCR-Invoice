using K_OCRLib.Data;
using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace KOCRAsp.Services;


public sealed class HomePageService : IHomePageService
{
    private static readonly string[] InvoiceExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    private readonly IBatchService _batchSvc;
    private readonly IFileService _fileSvc;
    private readonly IOrgDatabaseService _dbSvc;
    private readonly IInvoiceProcessingService _ocrSvc;
    private readonly IConfigurationService _configSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IInvoiceActionService _invoiceActionSvc;
    private readonly IOrganizationActivityLogService _orgLogSvc;
    private readonly IDbContextFactory<KOCRDbContext> _dbFactory;

    public HomePageService(
        IBatchService batchSvc,
        IFileService fileSvc,
        IOrgDatabaseService dbSvc,
        IInvoiceProcessingService ocrSvc,
        IConfigurationService configSvc,
        IOrgConfigService orgConfigSvc,
        IInvoiceActionService invoiceActionSvc,
        IOrganizationActivityLogService orgLogSvc,
        IDbContextFactory<KOCRDbContext> dbFactory)
    {
        _batchSvc = batchSvc;
        _fileSvc = fileSvc;
        _dbSvc = dbSvc;
        _ocrSvc = ocrSvc;
        _configSvc = configSvc;
        _orgConfigSvc = orgConfigSvc;
        _invoiceActionSvc = invoiceActionSvc;
        _orgLogSvc = orgLogSvc;
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<BatchSummary>> GetBatchesForOrgAsync(string orgId) =>
        await _batchSvc.GetBatchesForOrgAsync(orgId);

    public async Task<BatchSummary?> GetCurrentBatchAsync(string orgId, int? batchId)
    {
        if (!batchId.HasValue)
            return null;

        var batches = await _batchSvc.GetBatchesForOrgAsync(orgId);
        var batch = batches.FirstOrDefault(b => b.BatchId == batchId.Value);
        return batch is { IsMarkedForDeletion: false } ? batch : null;
    }

    public async Task<(List<FileListEntry> Items, int Total, int Page, int TotalPages)> BuildPagedFileListAsync(
        BatchSummary batch, int page, int pageSize, bool isGuestOrganization)
    {
        int maxPageCount = _configSvc.GetMaxPagesPerInvoice(isGuestOrganization);
        string invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        if (!Directory.Exists(invoicesFolder))
            return (new List<FileListEntry>(), 0, 1, 0);

        List<Invoice> invoices = await _dbSvc.GetInvoicesByBatchAsync(batch.BatchId);
        List<string> allFilePaths = invoices
            .Where(i => !string.IsNullOrWhiteSpace(i.FilePath))
            .Select(i => i.FilePath!)
            .ToList();

        int total = allFilePaths.Count;

        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        IEnumerable<string> pagePaths = allFilePaths.Skip((page - 1) * pageSize).Take(pageSize);
        List<FileListEntry> items = await BuildFileListForPathsAsync(pagePaths, maxPageCount);
        return (items, total, page, totalPages);
    }

    public async Task<UploadResult> UploadFilesAsync(
        int batchId,
        List<FileUpload> uploads,
        string userId,
        bool isGuestOrganization,
        string organizationName,
        string orgUser,
        string batchName)
    {
        int? maxInvoicesPerBatch = isGuestOrganization ? _configSvc.GetMaxInvoicesPerBatch(true) : null;
        var result = await _batchSvc.UploadFilesToBatchAsync(batchId, uploads, userId, maxInvoicesPerBatch);

        if (result.Success && result.FilesUploaded > 0)
        {
            var uploadedFileNames = uploads.Take(result.FilesUploaded).Select(u => u.FileName).ToArray();
            var uploadedNames = uploadedFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var upload in uploads)
            {
                if (uploadedNames.Contains(upload.FileName))
                    await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Added, upload.FileName, batchName, orgUser);
            }

            await _orgLogSvc.LogInvoiceUploadAsync(organizationName, batchName, orgUser, uploadedFileNames);
        }

        return result;
    }

    public async Task<(int Total, int Processed)> BuildOcrStatusAsync(BatchSummary batch, int batchId, int baseline)
    {
        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        var filePaths = Directory.Exists(invoicesFolder)
            ? _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions).ToList()
            : new List<string>();

        int allProcessed = (await _batchSvc.GetFullyProcessedFileNamesAsync(batchId)).Count;
        int total = filePaths.Count - baseline;
        int processed = allProcessed - baseline;
        return (total, processed);
    }

    public async Task<IReadOnlyList<string>> GetAlreadyOcrdFilesAsync(int batchId) =>
        await _batchSvc.GetFullyProcessedFileNamesAsync(batchId);

    public async Task<bool> RequiresBatchValidationForExportAsync(string orgName)
    {
        var orgConfig = await _orgConfigSvc.LoadAsync(orgName);
        return orgConfig.RequireBatchValidationForExport;
    }

    public async Task<BatchDetail?> GetBatchDetailAsync(int batchId) =>
        await _batchSvc.GetBatchDetailAsync(batchId);

    public async Task<InvoiceDto?> LoadInvoiceAsync(string filePath) =>
        await _ocrSvc.LoadInvoiceAsync(filePath);

    public async Task SaveInvoiceAsync(string filePath, InvoiceDto invoice) =>
        await _ocrSvc.SaveInvoiceAsync(filePath, invoice);

    public async Task AcceptValidationAsync(string filePath, string organizationName, string batchName, string orgUser)
    {
        var invoice = await _ocrSvc.LoadInvoiceAsync(filePath)
            ?? throw new InvalidOperationException("No processed invoice for this file.");

        invoice.IsInvoiceAccepted = true;
        await _ocrSvc.SaveInvoiceAsync(filePath, invoice);
        await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Validated, Path.GetFileName(filePath), batchName, orgUser);
        await _orgLogSvc.LogBatchValidatedAsync(organizationName, batchName, orgUser, Path.GetFileName(filePath));
    }

    public async Task<FileListEntry?> BuildInvoiceDotStateAsync(int invoiceId)
    {
        var invoice = await _dbSvc.GetInvoiceByIdAsync(invoiceId);
        if (invoice == null)
            return null;

        var entry = new FileListEntry
        {
            FilePath        = invoice.FilePath ?? string.Empty,
            FileName        = Path.GetFileName(invoice.FilePath ?? string.Empty),
            IsProcessed     = true,
            HasOcrResult    = invoice.IsFullyProcessed,
            IsSavedOrAccepted = invoice.IsInvoiceAccepted,
        };

        return entry;
    }

    public async Task<List<(string FileName, InvoiceDto Invoice)>> LoadBatchInvoicesAsync(BatchSummary batch)
    {
        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        if (!Directory.Exists(invoicesFolder))
            return [];

        var filePaths = _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions);
        var result = new List<(string, InvoiceDto)>();
        foreach (var fp in filePaths)
        {
            var inv = await _ocrSvc.LoadInvoiceAsync(fp);
            if (inv != null)
                result.Add((Path.GetFileName(fp), inv));
        }
        return result;
    }

    public async Task RemoveInvoiceFromBatchAsync(string filePath, int sourceBatchId, string batchName, string orgUser)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("File path required.");

        var fileExists = File.Exists(filePath);
        await using var db = _dbFactory.CreateDbContext();

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.BatchId == sourceBatchId && i.FilePath == filePath);
        var jobs = await db.OcrJobs
            .Where(j => j.BatchId == sourceBatchId && j.FilePath == filePath)
            .ToListAsync();

        if (!fileExists && invoice == null && jobs.Count == 0)
            throw new InvalidOperationException("Invoice file not found in the current batch.");

        if (fileExists)
            File.Delete(filePath);

        if (jobs.Count > 0)
            db.OcrJobs.RemoveRange(jobs);

        if (invoice != null)
            db.Invoices.Remove(invoice);

        await db.SaveChangesAsync();
        await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Removed, Path.GetFileName(filePath), batchName, orgUser);
    }

    public async Task MoveInvoiceToBatchAsync(string filePath, int sourceBatchId, int targetBatchId, string orgUser)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("File path required.");

        if (sourceBatchId == targetBatchId)
            throw new InvalidOperationException("Target batch must be different from the current batch.");

        var sourceBatch = await _batchSvc.GetBatchDetailAsync(sourceBatchId)
            ?? throw new InvalidOperationException("Current batch no longer exists.");
        var targetBatch = await _batchSvc.GetBatchDetailAsync(targetBatchId)
            ?? throw new InvalidOperationException("Target batch not found.");

        var sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException("Invoice file not found in the current batch.");

        var targetInvoicesDir = Path.Combine(targetBatch.FolderPath, "Invoices");
        Directory.CreateDirectory(targetInvoicesDir);

        var fileName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(targetInvoicesDir, fileName);
        if (File.Exists(targetPath))
            throw new InvalidOperationException($"Target batch already contains '{fileName}'.");

        await using var db = _dbFactory.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var fileMoved = false;

        try
        {
            File.Move(sourcePath, targetPath);
            fileMoved = true;

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.BatchId == sourceBatchId && i.FilePath == sourcePath);
            if (invoice != null)
            {
                invoice.BatchId = targetBatchId;
                invoice.FilePath = targetPath;
            }

            var jobs = await db.OcrJobs
                .Where(j => j.BatchId == sourceBatchId && j.FilePath == sourcePath)
                .ToListAsync();
            foreach (var job in jobs)
            {
                job.BatchId = targetBatchId;
                job.FilePath = targetPath;
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
                // Best-effort rollback; primary error is preserved below.
            }

            if (fileMoved && !File.Exists(sourcePath) && File.Exists(targetPath))
            {
                File.Move(targetPath, sourcePath);
            }

            throw new InvalidOperationException("Moving invoice failed and was rolled back.", ex);
        }

        await _invoiceActionSvc.LogAsync(
            InvoiceActionTypes.MovedToBatch,
            fileName,
            $"{sourceBatch.Name} -> {targetBatch.Name}",
            orgUser);
    }

    private async Task<List<FileListEntry>> BuildFileListForPathsAsync(IEnumerable<string> filePaths, int maxPageCount)
    {
        var entries = new List<FileListEntry>();
        foreach (var fp in filePaths)
        {
            var entry = new FileListEntry { FilePath = fp, FileName = Path.GetFileName(fp) };
            var invoice = await _dbSvc.GetInvoiceByFilePathAsync(fp);
            if (invoice != null)
            {
                entry.IsProcessed       = true;
                entry.TotalPages        = invoice.TotalPages;
                entry.HasOcrResult      = invoice.IsFullyProcessed;
                entry.IsSavedOrAccepted = invoice.IsInvoiceAccepted;
            }

            entry.ExceedsPageLimit = entry.TotalPages > maxPageCount;
            entries.Add(entry);
        }

        return entries;
    }
}
