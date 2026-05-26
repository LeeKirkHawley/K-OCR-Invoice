using System.Security.Claims;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Models;
using Newtonsoft.Json;

namespace KOCRAsp.Services;

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
        string orgUser,
        string batchName);
    Task<(int Total, int Processed)> BuildOcrStatusAsync(BatchSummary batch, int batchId, int baseline);
    Task<IReadOnlyList<string>> GetAlreadyOcrdFilesAsync(int batchId);
    Task<bool> RequiresBatchValidationForExportAsync(string orgName);
    Task<BatchDetail?> GetBatchDetailAsync(int batchId);
    Task<InvoiceDto?> LoadInvoiceAsync(string filePath);
    Task SaveInvoiceAsync(string filePath, InvoiceDto invoice);
    Task AcceptValidationAsync(string filePath, string batchName, string orgUser);
    Task<FileListEntry?> BuildInvoiceDotStateAsync(int invoiceId);
    Task<List<(string FileName, InvoiceDto Invoice)>> LoadBatchInvoicesAsync(BatchSummary batch);
}

public sealed class HomePageService : IHomePageService
{
    private static readonly string[] InvoiceExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    private readonly IBatchService _batchSvc;
    private readonly IFileService _fileSvc;
    private readonly DatabaseService _dbSvc;
    private readonly IInvoiceProcessingService _ocrSvc;
    private readonly IConfigurationService _configSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IInvoiceActionService _invoiceActionSvc;

    public HomePageService(
        IBatchService batchSvc,
        IFileService fileSvc,
        DatabaseService dbSvc,
        IInvoiceProcessingService ocrSvc,
        IConfigurationService configSvc,
        IOrgConfigService orgConfigSvc,
        IInvoiceActionService invoiceActionSvc)
    {
        _batchSvc = batchSvc;
        _fileSvc = fileSvc;
        _dbSvc = dbSvc;
        _ocrSvc = ocrSvc;
        _configSvc = configSvc;
        _orgConfigSvc = orgConfigSvc;
        _invoiceActionSvc = invoiceActionSvc;
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
        var maxPageCount = _configSvc.GetMaxPagesPerInvoice(isGuestOrganization);
        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        if (!Directory.Exists(invoicesFolder))
            return (new List<FileListEntry>(), 0, 1, 0);

        var allFilePaths = _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions).ToList();
        var total = allFilePaths.Count;

        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var pagePaths = allFilePaths.Skip((page - 1) * pageSize).Take(pageSize);
        var items = await BuildFileListForPathsAsync(pagePaths, maxPageCount);
        return (items, total, page, totalPages);
    }

    public async Task<UploadResult> UploadFilesAsync(
        int batchId,
        List<FileUpload> uploads,
        string userId,
        bool isGuestOrganization,
        string orgUser,
        string batchName)
    {
        int? maxInvoicesPerBatch = isGuestOrganization ? _configSvc.GetMaxInvoicesPerBatch(true) : null;
        var result = await _batchSvc.UploadFilesToBatchAsync(batchId, uploads, userId, maxInvoicesPerBatch);

        if (result.Success && result.FilesUploaded > 0)
        {
            var conflicts = result.ConflictingFileNames?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var upload in uploads)
            {
                if (!conflicts.Contains(upload.FileName))
                    await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Added, upload.FileName, batchName, orgUser);
            }
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

    public async Task AcceptValidationAsync(string filePath, string batchName, string orgUser)
    {
        var invoice = await _ocrSvc.LoadInvoiceAsync(filePath)
            ?? throw new InvalidOperationException("No processed invoice for this file.");

        invoice.IsValidationAccepted = true;
        await _ocrSvc.SaveInvoiceAsync(filePath, invoice);
        await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Validated, Path.GetFileName(filePath), batchName, orgUser);
    }

    public async Task<FileListEntry?> BuildInvoiceDotStateAsync(int invoiceId)
    {
        var invoice = await _dbSvc.GetInvoiceByIdAsync(invoiceId);
        if (invoice == null)
            return null;

        var entry = new FileListEntry
        {
            FilePath = invoice.FilePath ?? string.Empty,
            FileName = Path.GetFileName(invoice.FilePath ?? string.Empty),
            IsProcessed = true
        };

        if (!string.IsNullOrEmpty(invoice.ValidatedOcrText))
        {
            InvoiceDto? dto = null;
            try
            {
                dto = JsonConvert.DeserializeObject<InvoiceDto>(invoice.ValidatedOcrText);
            }
            catch
            {
                dto = JsonConvert.DeserializeObject<List<InvoiceDto>>(invoice.ValidatedOcrText)?.FirstOrDefault();
            }

            if (dto != null)
            {
                entry.IsSavedOrAccepted = dto.IsValidationAccepted;
                if (!entry.IsSavedOrAccepted && dto.TesseractConfirmed?.Count > 0)
                {
                    entry.HasSuspectFields =
                        dto.TesseractConfirmed.Values.Any(v => !v)
                        || (dto.MathConfirmed?.Values.Any(v => !v) ?? false)
                        || (dto.ConfidenceConfirmed?.Values.Any(v => !v) ?? false);
                    entry.IsValidated = !entry.HasSuspectFields;
                }
            }
        }

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

    private async Task<List<FileListEntry>> BuildFileListForPathsAsync(IEnumerable<string> filePaths, int maxPageCount)
    {
        var entries = new List<FileListEntry>();
        foreach (var fp in filePaths)
        {
            var entry = new FileListEntry { FilePath = fp, FileName = Path.GetFileName(fp) };
            var invoice = await _dbSvc.GetInvoiceByFilePathAsync(fp);
            if (invoice != null)
            {
                entry.IsProcessed = true;
                entry.TotalPages = invoice.TotalPages;
                if (!string.IsNullOrEmpty(invoice.ValidatedOcrText))
                {
                    InvoiceDto? dto = null;
                    try
                    {
                        dto = JsonConvert.DeserializeObject<InvoiceDto>(invoice.ValidatedOcrText);
                    }
                    catch
                    {
                        dto = JsonConvert.DeserializeObject<List<InvoiceDto>>(invoice.ValidatedOcrText)?.FirstOrDefault();
                    }

                    if (dto != null)
                    {
                        entry.IsSavedOrAccepted = dto.IsValidationAccepted;
                        if (!entry.IsSavedOrAccepted && dto.TesseractConfirmed?.Count > 0)
                        {
                            entry.HasSuspectFields =
                                dto.TesseractConfirmed.Values.Any(v => !v)
                                || (dto.MathConfirmed?.Values.Any(v => !v) ?? false)
                                || (dto.ConfidenceConfirmed?.Values.Any(v => !v) ?? false);
                            entry.IsValidated = !entry.HasSuspectFields;
                        }
                    }
                }
            }

            entry.ExceedsPageLimit = entry.TotalPages > maxPageCount;
            entries.Add(entry);
        }

        return entries;
    }
}
