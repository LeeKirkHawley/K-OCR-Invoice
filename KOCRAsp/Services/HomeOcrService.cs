using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using OCRQueue.Abstractions;

namespace KOCRAsp.Services;

public sealed record HomeTenantInfo(
    string OrganizationId,
    string OrganizationName,
    bool IsGuestOrganization,
    bool IsBetaTestOrganization,
    bool IsTrialOrganization,
    string StripeSubscriptionStatus,
    string? StripeCustomerId);

public sealed record HomeSingleOcrResult(
    bool Success,
    InvoiceDto? Invoice = null,
    bool Queued = false,
    int? InvoiceId = null,
    string? FilePath = null,
    string? Error = null,
    bool PageLimitExceeded = false);

public sealed record HomeBatchOcrResult(
    bool Success,
    bool AllSkipped = false,
    IReadOnlyList<string>? QueuedFilePaths = null,
    string? Error = null);

public sealed class HomeOcrService : IHomeOcrService
{
    private readonly IBatchService _batchSvc;
    private readonly IOrgDatabaseService _dbSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IConfigurationService _configSvc;
    private readonly IOcrEnqueueService _ocrEnqueueSvc;
    private readonly IOcrQueueRepository _ocrQueueRepo;
    private readonly IStripeUsageService _stripeUsage;
    private readonly ILogger<HomeOcrService> _logger;

    public HomeOcrService(
        IBatchService batchSvc,
        IOrgDatabaseService dbSvc,
        IOrgConfigService orgConfigSvc,
        IConfigurationService configSvc,
        IOcrEnqueueService ocrEnqueueSvc,
        IOcrQueueRepository ocrQueueRepo,
        IStripeUsageService stripeUsage,
        ILogger<HomeOcrService> logger)
    {
        _batchSvc = batchSvc;
        _dbSvc = dbSvc;
        _orgConfigSvc = orgConfigSvc;
        _configSvc = configSvc;
        _ocrEnqueueSvc = ocrEnqueueSvc;
        _ocrQueueRepo = ocrQueueRepo;
        _stripeUsage = stripeUsage;
        _logger = logger;
    }

    public bool CanUseOcr(HomeTenantInfo tenant) =>
        tenant.IsTrialOrganization || _stripeUsage.IsStatusActive(tenant.StripeSubscriptionStatus);

    public async Task<HomeSingleOcrResult> StartOcrAsync(
        string filePath,
        HomeTenantInfo tenant,
        string orgUser,
        BatchSummary? batch)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return new HomeSingleOcrResult(false, Error: "File path required.");

        if (!CanUseOcr(tenant))
            return new HomeSingleOcrResult(false, Error: "OCR is unavailable: subscription inactive.");

        if (string.IsNullOrWhiteSpace(tenant.OrganizationName))
            return new HomeSingleOcrResult(false, Error: "Organization context is missing.");

        var invoice0 = await _dbSvc.GetInvoiceByFilePathAsync(filePath);
        if (invoice0 is null)
            return new HomeSingleOcrResult(false, Error: "Invoice not found.");

        var maxPages = _configSvc.GetMaxPagesPerInvoice(tenant.IsGuestOrganization);
        if (invoice0.TotalPages > maxPages)
        {
            return new HomeSingleOcrResult(
                false,
                Error: $"This invoice has {invoice0.TotalPages} pages, which exceeds the limit of {maxPages}. It cannot be OCR'd.",
                PageLimitExceeded: true);
        }

        try
        {
            var pending = await _ocrQueueRepo.GetPendingJobsAsync(tenant.OrganizationName);
            var alreadyQueued = pending.Any(j =>
                j.BatchId == invoice0.BatchId &&
                string.Equals(j.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (alreadyQueued)
            {
                return new HomeSingleOcrResult(
                    true,
                    Queued: true,
                    InvoiceId: invoice0.Id,
                    FilePath: filePath);
            }

            var orgConfig = await _orgConfigSvc.LoadAsync(tenant.OrganizationName);
            var workflowKey = string.IsNullOrWhiteSpace(orgConfig.OcrWorkflowKey)
                ? "Default"
                : orgConfig.OcrWorkflowKey;

            var batchId = batch?.BatchId ?? invoice0.BatchId;
            var enqueued = await _ocrEnqueueSvc.EnqueueFilesAsync(
                [(filePath, invoice0.Id)],
                batchId,
                tenant.OrganizationId,
                tenant.OrganizationName,
                workflowKey);

            if (enqueued < 1)
                return new HomeSingleOcrResult(false, Error: "Failed to queue OCR job.");

            return new HomeSingleOcrResult(
                true,
                Queued: true,
                InvoiceId: invoice0.Id,
                FilePath: filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartOcr failed for {FilePath}", filePath);
            return new HomeSingleOcrResult(false, Error: ex.Message);
        }
    }

    public async Task<HomeBatchOcrResult> BatchOcrAsync(int batchId, bool skipAlreadyOcrd, HomeTenantInfo tenant)
    {
        if (!CanUseOcr(tenant))
            return new HomeBatchOcrResult(false, Error: "OCR is unavailable: subscription inactive.");

        try
        {
            double? minConfidence = null;
            string? workflowKey = null;
            if (!string.IsNullOrEmpty(tenant.OrganizationName))
            {
                var orgConfig = await _orgConfigSvc.LoadAsync(tenant.OrganizationName);
                minConfidence = orgConfig.MinConfidenceThreshold;
                workflowKey = orgConfig.OcrWorkflowKey;
            }

            int maxPageCount = _configSvc.GetMaxPagesPerInvoice(tenant.IsGuestOrganization);
            int? maxInvoicesPerBatch = tenant.IsGuestOrganization
                ? _configSvc.GetMaxInvoicesPerBatch(true)
                : null;

            ISet<string>? skipFileNames = null;
            if (skipAlreadyOcrd)
            {
                var processed = await _batchSvc.GetFullyProcessedFileNamesAsync(batchId);
                skipFileNames = new HashSet<string>(processed, StringComparer.OrdinalIgnoreCase);
            }

            var ocrResult = await _batchSvc.TriggerOcrAsync(
                batchId, minConfidence, skipFileNames, workflowKey, maxPageCount, maxInvoicesPerBatch);

            if (!ocrResult.Success)
                return new HomeBatchOcrResult(false, Error: ocrResult.ErrorMessage);

            return new HomeBatchOcrResult(
                true,
                AllSkipped: ocrResult.AllSkipped,
                QueuedFilePaths: ocrResult.QueuedFilePaths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchOcr failed for batch {BatchId}", batchId);
            return new HomeBatchOcrResult(false, Error: ex.Message);
        }
    }
}
