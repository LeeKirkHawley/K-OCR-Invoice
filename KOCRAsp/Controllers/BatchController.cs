using System.Security.Claims;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize]
public class BatchController : Controller
{
    private readonly IBatchService _batchSvc;
    private readonly ILogger<BatchController> _logger;

    public BatchController(IBatchService batchSvc, ILogger<BatchController> logger)
    {
        _batchSvc = batchSvc;
        _logger = logger;
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
                TempData["Success"] = $"Batch '{name}' created.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create batch failed");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int batchId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            await _batchSvc.DeleteBatchAsync(batchId, userId);
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
