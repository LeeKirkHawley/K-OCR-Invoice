using System.Security.Claims;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Security;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize]
public class BatchController : Controller
{
    private readonly IBatchService _batchSvc;
    private readonly IBatchChangeNotifier _batchNotifier;
    private readonly IBatchActionService _batchActionSvc;
    private readonly IBatchNotificationService _batchNotificationSvc;
    private readonly ILogger<BatchController> _logger;

    public BatchController(
        IBatchService batchSvc,
        IBatchChangeNotifier batchNotifier,
        IBatchActionService batchActionSvc,
        IBatchNotificationService batchNotificationSvc,
        ILogger<BatchController> logger)
    {
        _batchSvc             = batchSvc;
        _batchNotifier        = batchNotifier;
        _batchActionSvc       = batchActionSvc;
        _batchNotificationSvc = batchNotificationSvc;
        _logger               = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var batches = (await _batchSvc.GetBatchesForOrgAsync(orgId)).ToList();
        return View(batches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Batch name is required.";
            return RedirectToAction(nameof(Index));
        }

        var request = new CreateBatchRequest
        {
            Name = name,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            OrgName = User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty
        };

        try
        {
            var result = await _batchSvc.CreateBatchAsync(request);
            if (!result.Success)
                TempData["Error"] = result.ErrorMessage ?? "Failed to create batch.";
            else
            {
                TempData["Success"] = $"Batch '{name}' created.";
                _batchNotifier.Notify(User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty);
                await _batchActionSvc.LogAsync(
                    BatchActionTypes.Created, name,
                    User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
                    User.Identity?.Name ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create batch failed");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetNextBatchNumber()
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var next = await _batchSvc.GetNextBatchNumberAsync(orgId);
        return Json(new { nextBatchNumber = next });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAjax(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, error = "Batch name is required." });

        var request = new CreateBatchRequest
        {
            Name = name,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            OrgName = User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty
        };

        try
        {
            var result = await _batchSvc.CreateBatchAsync(request);
            if (!result.Success)
                return Json(new { success = false, error = result.ErrorMessage ?? "Failed to create batch." });

            _batchNotifier.Notify(User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty);
            await _batchActionSvc.LogAsync(
                BatchActionTypes.Created, name,
                User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
                User.Identity?.Name ?? string.Empty);
            return Json(new { success = true, batchId = result.BatchId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create batch (AJAX) failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int batchId)
    {
        var orgId = User.FindFirstValue(AppClaimTypes.OrganizationId) ?? string.Empty;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var batchName = await _batchSvc.DeleteBatchAsync(batchId, userId);
            _batchNotifier.Notify(orgId);
            await _batchActionSvc.LogAsync(
                BatchActionTypes.Deleted, batchName,
                User.FindFirstValue(AppClaimTypes.TenantName) ?? string.Empty,
                User.Identity?.Name ?? string.Empty);
            try
            {
                await _batchNotificationSvc.NotifyBatchSoftDeletedAsync(orgId, batchName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send soft-delete notification for batch '{Batch}' in org {OrgId}", batchName, orgId);
            }
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete batch {BatchId} failed", batchId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(int batchId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await _batchSvc.TryAcquireBatchLockAsync(batchId, userId);
            return Json(new { success = result.Success, error = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lock batch {BatchId} failed", batchId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int batchId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            await _batchSvc.ReleaseBatchLockAsync(batchId, userId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlock batch {BatchId} failed", batchId);
            return Json(new { success = false, error = ex.Message });
        }
    }
}
