using System.Security.Claims;
using System.Text;
using System.Linq;
using ClosedXML.Excel;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Models;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KOCRAsp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IHomePageService _homePageSvc;
    private readonly IHomeOcrService _homeOcrSvc;
    private readonly IHomeExportService _homeExportSvc;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<HomeController> _logger;

    private static readonly string[] InvoiceExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    public HomeController(
        IHomePageService homePageSvc,
        IHomeOcrService homeOcrSvc,
        IHomeExportService homeExportSvc,
        ITenantContext tenantContext,
        ILogger<HomeController> logger)
    {
        _homePageSvc = homePageSvc;
        _homeOcrSvc = homeOcrSvc;
        _homeExportSvc = homeExportSvc;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return RedirectToAction("Index", "Admin");

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;

        var batches = (await _homePageSvc.GetBatchesForOrgAsync(orgId)).ToList();

        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        BatchSummary? currentBatch = int.TryParse(currentBatchIdStr, out var currentBatchId)
            ? await _homePageSvc.GetCurrentBatchAsync(orgId, currentBatchId)
            : null;

        const int defaultPageSize = 25;
        var maxPagesPerInvoice = _tenantContext.IsGuestOrganization ? 20 : 20;
        var files = new List<FileListEntry>();
        int totalFiles = 0, currentPage = 1, totalPages = 0;
        if (currentBatch != null)
            (files, totalFiles, currentPage, totalPages) = await _homePageSvc.BuildPagedFileListAsync(
                currentBatch, 1, defaultPageSize, _tenantContext.IsGuestOrganization);

        return View(new HomeIndexViewModel
        {
            OrgId = orgId,
            AvailableBatches = batches,
            CurrentBatch = currentBatch,
            Files = files,
            TotalFiles = totalFiles,
            CurrentPage = currentPage,
            PageSize = defaultPageSize,
            TotalPages = totalPages,
            MaxPagesPerInvoice = maxPagesPerInvoice
        });
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false, error = "No batch selected." });

        var uploads = files
            .Select((f, index) =>
            {
                var clientPath = clientPaths?.ElementAtOrDefault(index);
                clientPath = string.IsNullOrWhiteSpace(clientPath) ? f.FileName : clientPath;
                return new FileUpload(f.FileName, clientPath, f.OpenReadStream());
            })
            .ToList();

        var batch = await GetCurrentBatchAsync();
        var result = await _homePageSvc.UploadFilesAsync(
            batchId, uploads, userId, _tenantContext.IsGuestOrganization,
            User.FindFirstValue(ClaimTypes.Email) ?? userId,
            batch?.Name ?? string.Empty);

        return Json(new
        {
            success = result.Success,
            filesUploaded = result.FilesUploaded,
            filesSkippedByLimit = result.FilesSkippedByLimit,
            error = result.ErrorMessage,
            conflicts = result.ConflictingFileNames
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
            _tenantContext.StripeSubscriptionStatus,
            _tenantContext.StripeCustomerId);

        var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var batch = await GetCurrentBatchAsync();
        var result = await _homeOcrSvc.StartOcrAsync(filePath, tenant, orgUser, batch);
        return Json(new
        {
            success = result.Success,
            pageLimitExceeded = result.PageLimitExceeded,
            error = result.Error,
            invoice = result.Invoice
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
            _tenantContext.StripeSubscriptionStatus,
            _tenantContext.StripeCustomerId);

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
        return Json(new { success = true, total, processed });
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
        return Json(new { items = files, total, page = actualPage, pageSize, totalPages });
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoice(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        var invoice = await _homePageSvc.LoadInvoiceAsync(filePath);
        if (invoice == null)
            return Json(new { success = false, error = "No processed invoice for this file." });

        return Json(new { success = true, invoice });
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
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
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
            await _homePageSvc.AcceptValidationAsync(filePath, batch?.Name ?? string.Empty, orgUser);

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
        var downloadName = Path.GetFileNameWithoutExtension(filePath) + ".docx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            downloadName);
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

        var batch = await GetCurrentBatchAsync();
        if (batch == null) return BadRequest("No batch selected.");

        var invoices = await _homePageSvc.LoadBatchInvoicesAsync(batch);
        var json = _homeExportSvc.BuildBatchJson(invoices);
        var bytes = Encoding.UTF8.GetBytes(json);
        var name = Path.GetFileName(batch.FolderPath.TrimEnd(Path.DirectorySeparatorChar));

        var orgUser = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await _homeExportSvc.LogBatchExportAsync(batch, invoices, orgUser);

        await _homeExportSvc.TrySoftDeleteBatchAfterExportAsync(
            batch,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
            orgUser);
        HttpContext.Session.Remove("CurrentBatchId");

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

        await _homeExportSvc.TrySoftDeleteBatchAfterExportAsync(
            batch,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty,
            User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
            orgUser);
        HttpContext.Session.Remove("CurrentBatchId");

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{name}.xlsx");
    }
}
