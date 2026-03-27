using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Models;
using KOCRAsp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KOCRAsp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IInvoiceProcessingService _ocrSvc;
    private readonly IBatchService _batchSvc;
    private readonly IFileService _fileSvc;
    private readonly IDocumentExportService _exportSvc;
    private readonly DatabaseService _dbSvc;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<HomeController> _logger;

    private static readonly string[] InvoiceExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    public HomeController(
        IInvoiceProcessingService ocrSvc,
        IBatchService batchSvc,
        IFileService fileSvc,
        IDocumentExportService exportSvc,
        DatabaseService dbSvc,
        IOrgConfigService orgConfigSvc,
        ITenantContext tenantContext,
        ILogger<HomeController> logger)
    {
        _ocrSvc        = ocrSvc;
        _batchSvc      = batchSvc;
        _fileSvc       = fileSvc;
        _exportSvc     = exportSvc;
        _dbSvc         = dbSvc;
        _orgConfigSvc  = orgConfigSvc;
        _tenantContext = tenantContext;
        _logger        = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return RedirectToAction("Index", "Admin");

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;

        var batches = (await _batchSvc.GetBatchesForOrgAsync(orgId)).ToList();

        BatchSummary? currentBatch = null;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (int.TryParse(currentBatchIdStr, out int currentBatchId))
            currentBatch = batches.FirstOrDefault(b => b.BatchId == currentBatchId);

        var files = new List<FileListEntry>();
        if (currentBatch != null)
            files = await BuildFileListAsync(currentBatch);

        return View(new HomeIndexViewModel
        {
            AvailableBatches = batches,
            CurrentBatch = currentBatch,
            Files = files
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
    public async Task<IActionResult> UploadFiles(List<IFormFile> files)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false, error = "No batch selected." });

        var uploads = files
            .Select(f => new FileUpload(f.FileName, f.OpenReadStream()))
            .ToList();

        var result = await _batchSvc.UploadFilesToBatchAsync(batchId, uploads, userId);
        return Json(new
        {
            success = result.Success,
            filesUploaded = result.FilesUploaded,
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

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        try
        {
            // File lives at {batchDir}/Invoices/{name} — artifacts belong at {batchDir}/Artifacts
            var invoicesDir  = Path.GetDirectoryName(filePath);
            var batchDir     = Path.GetDirectoryName(invoicesDir ?? string.Empty);
            var artifactsDir = !string.IsNullOrEmpty(batchDir)
                ? Path.Combine(batchDir, "Artifacts")
                : null;

            double? minConfidence = null;
            if (!string.IsNullOrEmpty(_tenantContext.OrganizationName))
            {
                var orgConfig = await _orgConfigSvc.LoadAsync(_tenantContext.OrganizationName);
                minConfidence = orgConfig.MinConfidenceThreshold;
            }

            var result = await _ocrSvc.ProcessFileAsync(filePath, useCache: false, artifactsDir, minConfidence);
            if (!result.IsSuccess)
                return Json(new { success = false, error = result.Error?.Message ?? "Processing failed." });

            var invoice = await _ocrSvc.LoadCachedInvoiceAsync(filePath);
            return Json(new { success = true, invoice });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartOcr failed for {FilePath}", filePath);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchOcr()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new { success = false, error = "No batch selected." });

        try
        {
            double? minConfidence = null;
            if (!string.IsNullOrEmpty(_tenantContext.OrganizationName))
            {
                var orgConfig = await _orgConfigSvc.LoadAsync(_tenantContext.OrganizationName);
                minConfidence = orgConfig.MinConfidenceThreshold;
            }

            await _batchSvc.TriggerOcrAsync(batchId, minConfidence);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchOcr failed for batch {BatchId}", batchId);
            return Json(new { success = false, error = ex.Message });
        }
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

        var batches = await _batchSvc.GetBatchesForOrgAsync(orgId);
        var batch = batches.FirstOrDefault(b => b.BatchId == batchId);
        if (batch == null) return Json(new { success = false });

        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        var filePaths = Directory.Exists(invoicesFolder)
            ? _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions).ToList()
            : new List<string>();

        int processed = filePaths.Count(fp => _ocrSvc.HasCachedResults(fp));
        return Json(new { success = true, total = filePaths.Count, processed });
    }

    [HttpGet]
    public async Task<IActionResult> GetFiles()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new List<FileListEntry>());

        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId))
            return Json(new List<FileListEntry>());

        var batches = await _batchSvc.GetBatchesForOrgAsync(orgId);
        var batch = batches.FirstOrDefault(b => b.BatchId == batchId);
        if (batch == null) return Json(new List<FileListEntry>());

        var files = await BuildFileListAsync(batch);
        return Json(files);
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoice(string filePath)
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Json(new { success = false, error = "Super-admin does not have org batch access." });

        if (string.IsNullOrWhiteSpace(filePath))
            return Json(new { success = false, error = "File path required." });

        var invoice = await _ocrSvc.LoadCachedInvoiceAsync(filePath);
        if (invoice == null)
            return Json(new { success = false, error = "No cached invoice for this file." });

        return Json(new { success = true, invoice });
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
            await _ocrSvc.SaveInvoiceAsync(request.FilePath, request.Invoice);
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
            var invoice = await _ocrSvc.LoadCachedInvoiceAsync(filePath);
            if (invoice == null)
                return Json(new { success = false, error = "No cached invoice for this file." });

            invoice.IsValidationAccepted = true;
            await _ocrSvc.SaveInvoiceAsync(filePath, invoice);
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

        var invoice = await _ocrSvc.LoadCachedInvoiceAsync(filePath);
        if (invoice == null)
            return NotFound("No cached invoice for this file.");

        var json = JsonConvert.SerializeObject(StripBboxFields(invoice));
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
        try
        {
            await _exportSvc.ExportToDocxAsync(json, tempPath);
            var bytes = await System.IO.File.ReadAllBytesAsync(tempPath);
            var downloadName = Path.GetFileNameWithoutExtension(filePath) + ".docx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                downloadName);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    [HttpGet]
    public IActionResult Error(int? statusCode)
    {
        ViewData["StatusCode"] = statusCode;
        return View();
    }

    private static JObject StripBboxFields(InvoiceDto invoice)
    {
        var jObj = JObject.FromObject(invoice);
        jObj.Remove("FieldBoundingBoxes");
        jObj.Remove("OriginalPageWidth");
        jObj.Remove("OriginalPageHeight");
        jObj.Remove("PageCount");
        if (jObj["Items"] is JArray items)
            foreach (var item in items.OfType<JObject>())
                item.Remove("BoundingBoxes");
        return jObj;
    }

    private async Task<List<FileListEntry>> BuildFileListAsync(BatchSummary batch)
    {
        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        if (!Directory.Exists(invoicesFolder))
            return new List<FileListEntry>();

        var filePaths = _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions);
        var entries = new List<FileListEntry>();

        foreach (var fp in filePaths)
        {
            var entry = new FileListEntry { FilePath = fp, FileName = Path.GetFileName(fp) };
            var invoice = await _dbSvc.GetInvoiceByFilePathAsync(fp);
            if (invoice != null)
            {
                entry.IsProcessed = true;
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
            entries.Add(entry);
        }

        return entries;
    }

    private async Task<BatchSummary?> GetCurrentBatchAsync()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var currentBatchIdStr = HttpContext.Session.GetString("CurrentBatchId");
        if (!int.TryParse(currentBatchIdStr, out int batchId)) return null;
        var batches = await _batchSvc.GetBatchesForOrgAsync(orgId);
        return batches.FirstOrDefault(b => b.BatchId == batchId);
    }

    private async Task<List<(string FileName, InvoiceDto Invoice)>> LoadBatchInvoicesAsync(BatchSummary batch)
    {
        var invoicesFolder = Path.Combine(batch.FolderPath, "Invoices");
        if (!Directory.Exists(invoicesFolder)) return new();
        var filePaths = _fileSvc.LoadFiles(invoicesFolder, InvoiceExtensions);
        var result = new List<(string, InvoiceDto)>();
        foreach (var fp in filePaths)
        {
            var inv = await _ocrSvc.LoadCachedInvoiceAsync(fp);
            if (inv != null) result.Add((Path.GetFileName(fp), inv));
        }
        return result;
    }

    [HttpGet]
    public async Task<IActionResult> ExportBatchJson()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var batch = await GetCurrentBatchAsync();
        if (batch == null) return BadRequest("No batch selected.");

        var invoices = await LoadBatchInvoicesAsync(batch);
        var payload = invoices.Select(t => new { fileName = t.FileName, invoice = StripBboxFields(t.Invoice) }).ToList();
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        var bytes = Encoding.UTF8.GetBytes(json);
        var name = Path.GetFileName(batch.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
        return File(bytes, "application/json", $"{name}.json");
    }

    [HttpGet]
    public async Task<IActionResult> ExportBatchExcel()
    {
        if (User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var batch = await GetCurrentBatchAsync();
        if (batch == null) return BadRequest("No batch selected.");

        var invoices = await LoadBatchInvoicesAsync(batch);

        using var wb = new XLWorkbook();

        // Sheet 1: header fields
        var ws = wb.Worksheets.Add("Invoices");
        string[] hdrs = ["File", "Vendor", "Customer", "Invoice #", "Invoice Date",
                         "Due Date", "PO #", "Subtotal", "Tax", "Shipping", "Total"];
        for (int c = 0; c < hdrs.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = hdrs[c];
            cell.Style.Font.Bold = true;
        }
        for (int r = 0; r < invoices.Count; r++)
        {
            var (fn, inv) = invoices[r];
            int row = r + 2;
            ws.Cell(row, 1).Value  = fn;
            ws.Cell(row, 2).Value  = inv.VendorName;
            ws.Cell(row, 3).Value  = inv.CustomerName;
            ws.Cell(row, 4).Value  = inv.InvoiceId;
            ws.Cell(row, 5).Value  = inv.InvoiceDate;
            ws.Cell(row, 6).Value  = inv.DueDate;
            ws.Cell(row, 7).Value  = inv.PurchaseOrder;
            ws.Cell(row, 8).Value  = inv.Subtotal.HasValue  ? (double)inv.Subtotal.Value  : (double?)null;
            ws.Cell(row, 9).Value  = inv.TotalTax.HasValue  ? (double)inv.TotalTax.Value  : (double?)null;
            ws.Cell(row, 10).Value = inv.Shipping.HasValue  ? (double)inv.Shipping.Value  : (double?)null;
            ws.Cell(row, 11).Value = inv.Total.HasValue     ? (double)inv.Total.Value     : (double?)null;
        }
        ws.Columns().AdjustToContents();

        // Sheet 2: line items
        var ws2 = wb.Worksheets.Add("Line Items");
        string[] hdrs2 = ["File", "Line #", "Description", "Quantity", "Unit Price", "Amount"];
        for (int c = 0; c < hdrs2.Length; c++)
        {
            var cell = ws2.Cell(1, c + 1);
            cell.Value = hdrs2[c];
            cell.Style.Font.Bold = true;
        }
        int row2 = 2;
        foreach (var (fn, inv) in invoices)
        {
            for (int i = 0; i < inv.Items.Count; i++)
            {
                var item = inv.Items[i];
                ws2.Cell(row2, 1).Value = fn;
                ws2.Cell(row2, 2).Value = i + 1;
                ws2.Cell(row2, 3).Value = item.Description;
                ws2.Cell(row2, 4).Value = item.Quantity.HasValue   ? (double)item.Quantity.Value   : (double?)null;
                ws2.Cell(row2, 5).Value = item.UnitPrice.HasValue  ? (double)item.UnitPrice.Value  : (double?)null;
                ws2.Cell(row2, 6).Value = item.Amount.HasValue     ? (double)item.Amount.Value     : (double?)null;
                row2++;
            }
        }
        ws2.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var name = Path.GetFileName(batch.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{name}.xlsx");
    }
}
