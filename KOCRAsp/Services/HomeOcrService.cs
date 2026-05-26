using K_OCR.Models;
using K_OCR.Services;

namespace KOCRAsp.Services;

public sealed record HomeTenantInfo(
    string OrganizationId,
    string OrganizationName,
    bool IsGuestOrganization,
    string StripeSubscriptionStatus,
    string? StripeCustomerId);

public sealed record HomeSingleOcrResult(
    bool Success,
    InvoiceDto? Invoice = null,
    string? Error = null,
    bool PageLimitExceeded = false);

public sealed record HomeBatchOcrResult(
    bool Success,
    bool AllSkipped = false,
    IReadOnlyList<string>? QueuedFilePaths = null,
    string? Error = null);

public interface IHomeOcrService
{
    bool CanUseOcr(HomeTenantInfo tenant);
    Task<HomeSingleOcrResult> StartOcrAsync(string filePath, HomeTenantInfo tenant, string orgUser, BatchSummary? batch);
    Task<HomeBatchOcrResult> BatchOcrAsync(int batchId, bool skipAlreadyOcrd, HomeTenantInfo tenant);
}

public sealed class HomeOcrService : IHomeOcrService
{
    private readonly IInvoiceProcessingService _ocrSvc;
    private readonly IBatchService _batchSvc;
    private readonly DatabaseService _dbSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IConfigurationService _configSvc;
    private readonly IInvoiceActionService _invoiceActionSvc;
    private readonly IReportingService _reportingSvc;
    private readonly IStripeUsageService _stripeUsage;
    private readonly ILogger<HomeOcrService> _logger;

    public HomeOcrService(
        IInvoiceProcessingService ocrSvc,
        IBatchService batchSvc,
        DatabaseService dbSvc,
        IOrgConfigService orgConfigSvc,
        IConfigurationService configSvc,
        IInvoiceActionService invoiceActionSvc,
        IReportingService reportingSvc,
        IStripeUsageService stripeUsage,
        ILogger<HomeOcrService> logger)
    {
        _ocrSvc = ocrSvc;
        _batchSvc = batchSvc;
        _dbSvc = dbSvc;
        _orgConfigSvc = orgConfigSvc;
        _configSvc = configSvc;
        _invoiceActionSvc = invoiceActionSvc;
        _reportingSvc = reportingSvc;
        _stripeUsage = stripeUsage;
        _logger = logger;
    }

    public bool CanUseOcr(HomeTenantInfo tenant) =>
        tenant.IsGuestOrganization || _stripeUsage.IsStatusActive(tenant.StripeSubscriptionStatus);

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

        var invoice0 = await _dbSvc.GetInvoiceByFilePathAsync(filePath);
        var maxPages = _configSvc.GetMaxPagesPerInvoice(tenant.IsGuestOrganization);
        if (invoice0 != null && invoice0.TotalPages > maxPages)
        {
            return new HomeSingleOcrResult(
                false,
                Error: $"This invoice has {invoice0.TotalPages} pages, which exceeds the limit of {maxPages}. It cannot be OCR'd.",
                PageLimitExceeded: true);
        }

        try
        {
            var invoicesDir = Path.GetDirectoryName(filePath);
            var batchDir = Path.GetDirectoryName(invoicesDir ?? string.Empty);
            var artifactsDir = !string.IsNullOrEmpty(batchDir)
                ? Path.Combine(batchDir, "Artifacts")
                : null;

            double? minConfidence = null;
            if (!string.IsNullOrEmpty(tenant.OrganizationName))
            {
                var orgConfig = await _orgConfigSvc.LoadAsync(tenant.OrganizationName);
                minConfidence = orgConfig.MinConfidenceThreshold;
            }

            var result = await _ocrSvc.ProcessFileAsync(filePath, artifactsDir, minConfidence);
            if (!result.IsSuccess)
                return new HomeSingleOcrResult(false, Error: result.Error?.Message ?? "Processing failed.");

            var invoice = await _ocrSvc.LoadInvoiceAsync(filePath);
            await _invoiceActionSvc.LogAsync(
                InvoiceActionTypes.OCRed,
                Path.GetFileName(filePath),
                batch?.Name ?? string.Empty,
                orgUser,
                invoice?.PageCount ?? 1);

            try
            {
                await _reportingSvc.RecordBatchOcrEventAsync(new BatchOcrReportRequest
                {
                    OrganizationId = tenant.OrganizationId,
                    OrganizationName = tenant.OrganizationName,
                    BatchName = batch?.Name ?? string.Empty,
                    Invoices =
                    [
                        new OcrInvoiceResult
                        {
                            FileName = Path.GetFileName(filePath),
                            OcrSucceeded = result.IsSuccess,
                            OcrService = "Azure",
                            PageCount = invoice?.PageCount ?? 1
                        }
                    ]
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record single-file OCR report for {FilePath}.", filePath);
            }

            if (!tenant.IsGuestOrganization && tenant.StripeCustomerId is { } customerId)
            {
                var idempotencyKey = $"file-{Path.GetFileName(filePath)}-{Guid.NewGuid():N}";
                await _stripeUsage.ReportUsageAsync(customerId, invoice?.PageCount ?? 1, idempotencyKey);
            }

            return new HomeSingleOcrResult(true, Invoice: invoice);
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
