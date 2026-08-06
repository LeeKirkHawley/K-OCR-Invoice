using K_OCRLib.Configuration;
using K_OCRLib.Data;
using K_OCRLib.Identity;
using K_OCRLib.Models;
using K_OCRLib.Security;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Models;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using OCRQueue.Abstractions;
using System.Security.Claims;
using System.Text;

namespace KOCRAsp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IHomePageService _homePageSvc;
    private readonly IHomeOcrService _homeOcrSvc;
    private readonly IHomeExportService _homeExportSvc;
    private readonly IOrganizationActivityLogService _orgLogSvc;
    private readonly ITrialOrganizationLimitService _trialLimitSvc;
    private readonly ITenantContext _tenantContext;
    private readonly IConfigurationService _configSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IOcrQueueRepository _ocrQueueRepo;
    private readonly IOrgDatabaseService _databaseService;
    private readonly ILogger<HomeController> _logger;
    private readonly K_OCRLib.Services.IBatchChangeNotifier _batchNotifier;


    public HomeController(
        IHomePageService homePageSvc,
        IHomeOcrService homeOcrSvc,
        IHomeExportService homeExportSvc,
        IOrganizationActivityLogService orgLogSvc,
        ITrialOrganizationLimitService trialLimitSvc,
        ITenantContext tenantContext,
        IConfigurationService configSvc,
        IOrgConfigService orgConfigSvc,
        IOcrQueueRepository ocrQueueRepo,
        IOrgDatabaseService databaseService,
        ILogger<HomeController> logger,
        IBatchChangeNotifier batchNotifier)
    {
        _homePageSvc = homePageSvc;
        _homeOcrSvc = homeOcrSvc;
        _homeExportSvc = homeExportSvc;
        _orgLogSvc = orgLogSvc;
        _trialLimitSvc = trialLimitSvc;
        _tenantContext = tenantContext;
        _configSvc = configSvc;
        _orgConfigSvc = orgConfigSvc;
        _ocrQueueRepo = ocrQueueRepo;
        _databaseService = databaseService;
        _logger = logger;
        _batchNotifier = batchNotifier;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        if (!string.Equals(Request.Path.Value, "/", StringComparison.Ordinal))
            return Redirect("/");

        if (User.Identity?.IsAuthenticated != true)
        {
            ViewData["Title"] = "Hardscrabble Invoice";
            ViewData["HideNav"] = true;
            ViewData["AllowIndexing"] = true;
            return View("Landing");
        }

        // SuperAdmins don't have an org; skip validation.
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            // Validate that the cached org is still valid.
            // If the org was deleted, sign out and redirect to login.
            if (!await ValidateOrgAsync())
            {
                _logger.LogWarning("Org validation failed; signing out and redirecting to login.");
                await HttpContext.SignOutAsync();

                // Explicitly clear the auth cookie to ensure immediate sign-out
                HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");

                return Redirect("/auth/login");
            }
        }
        else
        {
            return RedirectToAction("Index", "Admin");
        }

        string orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        string? currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");


        List<BatchSummary> batches = (await _homePageSvc.GetBatchesForOrgAsync(orgId)).ToList();

        var batchInfo = await GetBatchInfo(orgId, currentBatchIdStr, batches);
        return batchInfo;
    }

    private async Task<IActionResult> GetBatchInfo(string orgId, string? currentBatchIdStr, List<BatchSummary> batches)
    {
        BatchSummary? currentBatch = int.TryParse(currentBatchIdStr, out var currentBatchId)
            ? await _homePageSvc.GetCurrentBatchAsync(orgId, currentBatchId)
            : null;

        const int defaultPageSize = 25;
        int maxInvoicesPerBatch = _configSvc.GetMaxInvoicesPerBatch(_tenantContext.IsGuestOrganization);
        int maxPagesPerInvoice = _configSvc.GetMaxPagesPerInvoice(_tenantContext.IsGuestOrganization);
        List<FileListEntry> files = new List<FileListEntry>();
        int totalFiles = 0, currentPage = 1, totalPages = 0;
        if (currentBatch != null)
            (files, totalFiles, currentPage, totalPages) = await _homePageSvc.BuildPagedFileListAsync(
                currentBatch, 1, defaultPageSize, _tenantContext.IsGuestOrganization);

        TrialOrganizationLimitStatus betaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        bool isOrgAdmin = User.IsActiveOrganizationAdmin();
        bool isGuest = _tenantContext.IsGuestOrganization;

        OrgConfig orgConfig = _tenantContext.OrganizationName is { } orgName
            ? await _orgConfigSvc.LoadAsync(orgName)
            : new OrgConfig();

        var model = new HomeIndexViewModel
        {
            OrgId = orgId,
            CurrentUserEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
            AvailableBatches = batches,
            CurrentBatch = currentBatch,
            Files = files,
            TotalFiles = totalFiles,
            CurrentPage = currentPage,
            PageSize = defaultPageSize,
            TotalPages = totalPages,
            MaxPagesPerInvoice = maxPagesPerInvoice,
            MaxInvoicesPerBatch = maxInvoicesPerBatch,
            IsBetaTestOrganization = betaStatus.IsBetaTestOrganization,
            IsOrgAdmin = isOrgAdmin,
            ShowBetaWelcomeDialog = betaStatus.IsBetaTestOrganization && isOrgAdmin,
            BetaMaxOcrPages = betaStatus.MaxOcrPages,
            BetaUsedOcrPages = betaStatus.UsedOcrPages,
            BetaRemainingOcrPages = betaStatus.RemainingOcrPages,
            BetaOcrLimitExceeded = betaStatus.IsOcrLimitExceeded(),
            IsGuestOrganization = betaStatus.IsGuestOrganization,
            ShowGuestWelcomeDialog = betaStatus.IsGuestOrganization && isOrgAdmin,
            GuestMaxBatches = betaStatus.MaxBatches,
            GuestActiveBatchCount = betaStatus.UsedBatches,
            GuestRemainingBatches = betaStatus.RemainingBatches,
            GuestBatchLimitExceeded = betaStatus.IsBatchLimitExceeded(),
            GuestMaxInvoicesPerBatch = isGuest ? _configSvc.GetMaxInvoicesPerBatch(isGuest: true) : 0,
            GuestMaxPagesPerInvoice = isGuest ? _configSvc.GetMaxPagesPerInvoice(isGuest: true) : 0,
            MinConfidenceThreshold = orgConfig.MinConfidenceThreshold,
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SelectBatch(int batchId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        HttpContext.Session.SetString("CurrentBatchId", batchId.ToString());
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFiles(List<IFormFile> files, List<string>? clientPaths)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        // should be able to get both guest and beta information here
        TrialOrganizationLimitStatus betaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        if (betaStatus.IsOcrLimitExceeded())
        {
            return Json(new
            {
                success = false,
                betaLimitExceeded = true,
                error = $"This organization has reached its OCR limit ({betaStatus.UsedOcrPages}/{betaStatus.MaxOcrPages} pages). Uploads and OCR are disabled."
            });
        }

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        string? currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false, error = "No batch selected." });

        List<FileUpload> uploads = files
            .Select((f, index) =>
            {
                var clientPath = clientPaths?.ElementAtOrDefault(index);
                clientPath = string.IsNullOrWhiteSpace(clientPath) ? f.FileName : clientPath;
                // Ensure server-side filename does not contain directory components supplied by the client
                var safeFileName = Path.GetFileName(f.FileName);
                return new FileUpload(safeFileName, clientPath, f.OpenReadStream());
            })
            .ToList();

        BatchSummary? batch = await GetCurrentBatchAsync();

        UploadResult result = await _homePageSvc.UploadFilesAsync(
            batchId, uploads, userId, _tenantContext.IsGuestOrganization, _tenantContext.OrganizationName ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Email) ?? userId,
            batch?.Name ?? string.Empty);

        var uploadedFileNames = uploads.Take(result.FilesUploaded).Select(u => u.FileName).ToArray();

        // Notify other clients in this org that the batch changed (files added)
        try
        {
            if (result.Success && result.FilesUploaded > 0)
            {
                var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(orgId))
                {
                    _batchNotifier.Notify(orgId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify batch change after upload");
        }

        return Json(new
        {
            success = result.Success,
            filesUploaded = result.FilesUploaded,
            filesSkippedByLimit = result.FilesSkippedByLimit,
            error = result.ErrorMessage,
            conflicts = result.ConflictingFileNames,
            uploadedFileNames = uploadedFileNames
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartOcr(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        var tenant = new HomeTenantInfo(
            _tenantContext.OrganizationId ?? string.Empty,
            _tenantContext.OrganizationName ?? string.Empty,
            _tenantContext.IsGuestOrganization,
            _tenantContext.IsBetaTestOrganization,
            _tenantContext.IsTrialOrganization,
            _tenantContext.StripeSubscriptionStatus,
            _tenantContext.StripeCustomerId);

        var betaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        if (betaStatus.IsOcrLimitExceeded())
        {
            return Json(new
            {
                success = false,
                betaLimitExceeded = true,
                error = $"This beta-test organization has reached its OCR limit ({betaStatus.UsedOcrPages}/{betaStatus.MaxOcrPages} pages)."
            });
        }

        var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var batch = await GetCurrentBatchAsync();
        var result = await _homeOcrSvc.StartOcrAsync(filePath, tenant, orgUser, batch);
        var updatedBetaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        return Json(new
        {
            success = result.Success,
            queued = result.Queued,
            invoiceId = result.InvoiceId,
            filePath = result.FilePath,
            pageLimitExceeded = result.PageLimitExceeded,
            error = result.Error,
            invoice = result.Invoice,
            betaLimitExceeded = updatedBetaStatus.IsOcrLimitExceeded(),
            betaPagesRemaining = updatedBetaStatus.RemainingOcrPages,
            betaUsedPages = updatedBetaStatus.UsedOcrPages,
            betaMaxPages = updatedBetaStatus.MaxOcrPages,
            guestActiveBatchCount = updatedBetaStatus.UsedBatches,
            guestRemainingBatches = updatedBetaStatus.RemainingBatches,
            guestBatchLimitExceeded = updatedBetaStatus.IsBatchLimitExceeded()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchOcr(bool skipAlreadyOcrd = false)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false, error = "No batch selected." });

        var tenant = new HomeTenantInfo(
            _tenantContext.OrganizationId ?? string.Empty,
            _tenantContext.OrganizationName ?? string.Empty,
            _tenantContext.IsGuestOrganization,
            _tenantContext.IsBetaTestOrganization,
            _tenantContext.IsTrialOrganization,
            _tenantContext.StripeSubscriptionStatus,
            _tenantContext.StripeCustomerId);

        var betaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        if (betaStatus.IsOcrLimitExceeded())
        {
            return Json(new
            {
                success = false,
                betaLimitExceeded = true,
                error = $"This beta-test organization has reached its OCR limit ({betaStatus.UsedOcrPages}/{betaStatus.MaxOcrPages} pages)."
            });
        }

        if (betaStatus.IsBatchLimitExceeded())
        {
            return Json(new
            {
                success = false,
                guestBatchLimitExceeded = true,
                error = $"This guest organization has reached its batch limit ({betaStatus.UsedBatches}/{betaStatus.MaxBatches} batches)."
            });
        }

        var result = await _homeOcrSvc.BatchOcrAsync(batchId, skipAlreadyOcrd, tenant);
        var baseline = skipAlreadyOcrd ? (await _homePageSvc.GetAlreadyOcrdFilesAsync(batchId)).Count : 0;
        HttpContext.Session.SetInt32("OcrSkipBaseline", baseline);
        return Json(new
        {
            success = result.Success,
            allSkipped = result.AllSkipped,
            queuedFilePaths = result.QueuedFilePaths,
            error = result.Error
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetOcrStatus()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false });

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false });

        var batch = await _homePageSvc.GetCurrentBatchAsync(orgId, batchId);
        if (batch == null) return Json(new { success = false });

        int baseline = HttpContext.Session.GetInt32("OcrSkipBaseline") ?? 0;
        var (total, processed) = await _homePageSvc.BuildOcrStatusAsync(batch, batchId, baseline);
        var betaStatus = await _trialLimitSvc.GetCurrentStatusAsync();
        return Json(new
        {
            success = true,
            total,
            processed,
            betaLimitExceeded = betaStatus.IsOcrLimitExceeded(),
            betaPagesRemaining = betaStatus.RemainingOcrPages,
            betaUsedPages = betaStatus.UsedOcrPages,
            betaMaxPages = betaStatus.MaxOcrPages,
            guestActiveBatchCount = betaStatus.UsedBatches,
            guestRemainingBatches = betaStatus.RemainingBatches,
            guestBatchLimitExceeded = betaStatus.IsBatchLimitExceeded()
        });
    }

    /// <summary>
    /// Returns the current dot-state for a single invoice so the Home page
    /// can update coloured status indicators in real-time via SignalR.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> InvoiceDotState(int invoiceId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { error = "Not available for super-admin." });

        var entry = await _homePageSvc.BuildInvoiceDotStateAsync(invoiceId);
        if (entry == null)
            return Json(new { error = "Invoice not found." });

        return Json(new
        {
            filePath           = entry.FilePath,
            processedDotClass  = entry.ProcessedDotClass,
            processedDotTitle  = entry.ProcessedDotTitle,
            validationDotClass = entry.ValidationDotClass,
            validationDotTitle = entry.ValidationDotTitle,
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetAlreadyOcrdFiles()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { files = Array.Empty<string>() });

        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { files = Array.Empty<string>() });

        var files = await _homePageSvc.GetAlreadyOcrdFilesAsync(batchId);
        return Json(new { files });
    }

    [HttpGet]
    public async Task<IActionResult> GetBatches()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new List<object>());

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var batches = (await _homePageSvc.GetBatchesForOrgAsync(orgId))
            .Where(b => !b.IsMarkedForDeletion)
            .ToList();
        
        var result = batches.Select(b => new
        {
            batchId = b.BatchId,
            name = $"{b.Name} (#{b.BatchNumber})"
        }).ToList();
        
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetBatchExportStatus()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { requiresValidation = false });

        var requiresValidation = await _homePageSvc.RequiresBatchValidationForExportAsync(_tenantContext.OrganizationName ?? string.Empty);
        if (!requiresValidation)
            return Json(new { requiresValidation = false });

        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { requiresValidation = true, allValidated = false, fileCount = 0, validatedCount = 0 });

        var detail = await _homePageSvc.GetBatchDetailAsync(batchId);
        if (detail == null)
            return Json(new { requiresValidation = true, allValidated = false, fileCount = 0, validatedCount = 0 });

        return Json(new
        {
            requiresValidation = true,
            allValidated       = detail.AllValidated,
            fileCount          = detail.FileCount,
            validatedCount     = detail.ValidatedCount,
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetFiles(int page = 1, int pageSize = 25)
    {
        static object EmptyResult(int ps) =>
            new { items = Array.Empty<FileListEntry>(), total = 0, page = 1, pageSize = ps, totalPages = 0 };

        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(EmptyResult(pageSize));

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(EmptyResult(pageSize));

        var batch = await _homePageSvc.GetCurrentBatchAsync(orgId, batchId);
        if (batch == null) return Json(EmptyResult(pageSize));

        var (files, total, actualPage, totalPages) = await _homePageSvc.BuildPagedFileListAsync(
            batch, page, pageSize, _tenantContext.IsGuestOrganization);
        var queuedFilePaths = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(_tenantContext.OrganizationName))
        {
            var pendingJobs = await _ocrQueueRepo.GetPendingJobsAsync(_tenantContext.OrganizationName);
            queuedFilePaths = pendingJobs
                .Where(j => j.BatchId == batchId && !string.IsNullOrWhiteSpace(j.FilePath))
                .Select(j => j.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Json(new { items = files, total, page = actualPage, pageSize, totalPages, queuedFilePaths });
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoice(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        InvoiceDto? invoice = await _homePageSvc.LoadInvoiceAsync(filePath);
        if (invoice == null)
            return Json(new { success = false, error = "No processed invoice for this file." });

        JsonResult result = Json(new { success = true, invoice });

        return result;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveInvoiceFromBatch(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        var batch = await GetCurrentBatchAsync();
        if (batch == null)
            return Json(new { success = false, error = "No batch selected." });

        try
        {
            var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await _homePageSvc.RemoveInvoiceFromBatchAsync(filePath, batch.BatchId, batch.Name, orgUser);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveInvoiceFromBatch failed for {FilePath}", filePath);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveInvoiceToBatch(string filePath, int targetBatchId)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        if (targetBatchId <= 0)
            return Json(new { success = false, error = "Target batch required." });

        var sourceBatch = await GetCurrentBatchAsync();
        if (sourceBatch == null)
            return Json(new { success = false, error = "No batch selected." });

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var targetBatch = (await _homePageSvc.GetBatchesForOrgAsync(orgId))
            .FirstOrDefault(b => b.BatchId == targetBatchId && !b.IsMarkedForDeletion);
        if (targetBatch == null)
            return Json(new { success = false, error = "Target batch not found." });

        try
        {
            var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await _homePageSvc.MoveInvoiceToBatchAsync(filePath, sourceBatch.BatchId, targetBatchId, orgUser);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveInvoiceToBatch failed for {FilePath} -> {TargetBatchId}", filePath, targetBatchId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveInvoice()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        SaveInvoiceRequest? request;
        try
        {
            using StreamReader reader = new StreamReader(Request.Body);
            string body = await reader.ReadToEndAsync();
            request = JsonConvert.DeserializeObject<SaveInvoiceRequest>(body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveInvoice: failed to parse request body");
            return Json(new { success = false, error = "Invalid request body." });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.FilePath) || request.Invoice == null)
            return Json(new { success = false, error = "Invalid request." });

        try
        {
            await _homePageSvc.SaveInvoiceAsync(request.FilePath, request.Invoice);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveInvoice failed for {FilePath}", request.FilePath);
            return Json(new { success = false, error = ex.Message });
        }
    }


    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveInvoiceEdits()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        FieldEditDTO request;
        try
        {
            using StreamReader reader = new StreamReader(Request.Body);
            string body = await reader.ReadToEndAsync();
            request = JsonConvert.DeserializeObject<FieldEditDTO>(body);

            _databaseService.SaveInvoiceEdits(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveInvoiceEdits: failed to parse request body");
            return Json(new { success = false, error = "Invalid request body." });
        }



        return Json(new { success = true });

        //if (request == null || string.IsNullOrWhiteSpace(request.FilePath) || request.Invoice == null)
        //    return Json(new { success = false, error = "Invalid request." });

        //try
        //{
        //    await _homePageSvc.SaveInvoiceAsync(request.FilePath, request.Invoice);
        //    return Json(new { success = true });
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "SaveInvoice failed for {FilePath}", request.FilePath);
        //    return Json(new { success = false, error = ex.Message });
        //}
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptValidation(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        try
        {
            var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var batch   = await GetCurrentBatchAsync();
            await _homePageSvc.AcceptValidationAsync(filePath, _tenantContext.OrganizationName ?? string.Empty, batch?.Name ?? string.Empty, orgUser);

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AcceptValidation failed for {FilePath}", filePath);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportDocx(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        if (string.IsNullOrWhiteSpace(filePath))
            return BadRequest("File path required.");

        var invoice = await _homePageSvc.LoadInvoiceAsync(filePath);
        if (invoice == null)
            return NotFound("No processed invoice for this file.");

        var bytes = await _homeExportSvc.BuildDocxAsync(filePath, invoice);
        var fileName = Path.GetFileName(filePath);
        var batchName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath) ?? string.Empty) ?? string.Empty);
        await _orgLogSvc.LogValidatedInvoiceDownloadAsync(
            _tenantContext.OrganizationName ?? string.Empty,
            batchName,
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            fileName);
        var downloadName = Path.GetFileNameWithoutExtension(filePath) + ".docx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            downloadName);
    }

    /// <summary>
    /// Returns the centralized field registry (header and item fields).
    /// Used by the frontend to identify and handle fields consistently with the backend.
    /// </summary>
    [HttpGet]
    public IActionResult GetFieldRegistry()
    {
        return Json(new
        {
            headerFields = FieldRegistry.HeaderFields,
            itemFields = FieldRegistry.ItemFields,
            moneyPropertyNames = FieldRegistry.MoneyPropertyNames.ToList(),
        });
    }

    [HttpGet]
    public IActionResult Error(int? statusCode)
    {
        ViewData["StatusCode"] = statusCode;
        return View();
    }

    private async Task<BatchSummary?> GetCurrentBatchAsync()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        return int.TryParse(currentBatchIdStr, out int batchId)
            ? await _homePageSvc.GetCurrentBatchAsync(orgId, batchId)
            : null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportBatchJson()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        BatchSummary? batch = await GetCurrentBatchAsync();
        if (batch == null) 
            return BadRequest("No batch selected.");

        List<(string FileName, InvoiceDto Invoice)> invoices = await _homePageSvc.LoadBatchInvoicesAsync(batch);

        string json = _homeExportSvc.BuildBatchJson(invoices);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string name = Path.GetFileName(batch.FolderPath.TrimEnd(Path.DirectorySeparatorChar));

        string orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await _homeExportSvc.LogBatchExportAsync(batch, invoices, orgUser);

        // Soft-deleting the batch happens only after the browser confirms the
        // exported file was actually written to disk (see ConfirmBatchExported).
        // Don't clear the session or mark the batch for deletion here — the
        // client may still cancel the save dialog or fail to write the file.
        return File(bytes, "application/json", $"{name}.json");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportBatchExcel()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var batch = await GetCurrentBatchAsync();
        if (batch == null) return BadRequest("No batch selected.");

        var invoices = await _homePageSvc.LoadBatchInvoicesAsync(batch);
        var bytes = _homeExportSvc.BuildBatchExcel(invoices);
        var name = Path.GetFileName(batch.FolderPath.TrimEnd(Path.DirectorySeparatorChar));

        var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await _homeExportSvc.LogBatchExportAsync(batch, invoices, orgUser);

        // Soft-deleting the batch happens only after the browser confirms the
        // exported file was actually written to disk (see ConfirmBatchExported).
        // Don't clear the session or mark the batch for deletion here — the
        // client may still cancel the save dialog or fail to write the file.
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{name}.xlsx");
    }

    // Called by the front end only after it has confirmed that the exported
    // file (JSON or Excel) was successfully written to the user-chosen
    // location on disk. This is what actually marks the batch for deletion,
    // so a cancelled save dialog or a failed write never deletes a batch.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBatchExported()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var batch = await GetCurrentBatchAsync();
        if (batch == null) return BadRequest("No batch selected.");

        var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        await _homeExportSvc.TrySoftDeleteBatchAfterExportAsync(
            batch,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
            orgUser);
        HttpContext.Session.Remove("CurrentBatchId");

        return Json(new { success = true });
    }

    /// <summary>
    /// Validates that the organization from the authentication cookie still exists
    /// in the database and is active. Returns false if the org has been deleted or deactivated,
    /// or if validation cannot be performed.
    /// </summary>
    private async Task<bool> ValidateOrgAsync()
    {
        string? orgId = User.FindFirstValue(AppClaimTypes.OrganizationId);

        // No org claim means validation fails.
        if (string.IsNullOrWhiteSpace(orgId))
            return false;

        try
        {
            // Try to get ApplicationDbContext from RequestServices
            if (HttpContext.RequestServices.GetService(typeof(ApplicationDbContext)) is not ApplicationDbContext appDbContext)
            {
                // If DbContext not available, fail closed
                _logger.LogWarning("ApplicationDbContext not available in RequestServices; cannot validate org {OrgId}", orgId);
                return false;
            }

            Organization? org = await appDbContext.Organizations.FindAsync(orgId);
            
            if (org == null)
            {
                _logger.LogWarning("Organization {OrgId} not found; forcing sign-out.", orgId);
                return false;
            }

            if (!org.IsActive)
            {
                _logger.LogWarning("Organization {OrgId} is inactive; forcing sign-out.", orgId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error validating organization {OrgId}; forcing sign-out.",
                orgId);
            return false;
        }
    }
}
