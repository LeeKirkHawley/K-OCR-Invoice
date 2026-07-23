using K_OCRLib.Identity;
using K_OCRLib.Models;
using K_OCRLib.Models.Api.SuperAdmin;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCRQueue.Abstractions;

namespace KOCRAsp.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class AdminController : Controller
{
    private readonly ISuperAdminService _superAdminSvc;
    private readonly ISuperAdminDataService _dataSvc;
    private readonly IOrganizationAdminService _orgAdminSvc;
    private readonly IConfiguration _configuration;
    private readonly IOcrJobQueue _ocrJobQueue;
    private readonly IOcrQueueProcessor _ocrQueueProcessor;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ISuperAdminService superAdminSvc,
        ISuperAdminDataService dataSvc,
        IOrganizationAdminService orgAdminSvc,
        IConfiguration configuration,
        IOcrJobQueue ocrJobQueue,
        IOcrQueueProcessor ocrQueueProcessor,
        ILogger<AdminController> logger)
    {
        _superAdminSvc      = superAdminSvc;
        _dataSvc            = dataSvc;
        _orgAdminSvc        = orgAdminSvc;
        _configuration      = configuration;
        _ocrJobQueue        = ocrJobQueue;
        _ocrQueueProcessor  = ocrQueueProcessor;
        _logger             = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orgs = await _superAdminSvc.ListOrganizationsAsync();
        int value = 0;
        try
        {
            value = _configuration.GetValue<int>("DeletedOrgRetentionDays");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read DeletedOrgRetentionDays from configuration. Defaulting to 14 days.");
            value = 14; // default fallback
        }
        ViewBag.DeletedOrgRetentionDays = value;
        ViewBag.DefaultBetaMaxOcrPages = _configuration.GetValue<int?>("Limits:Beta:MaxOcrPages") ?? 500;
        ViewBag.QueueIsPaused = _ocrQueueProcessor.IsPaused;
        return View(orgs);
    }

    [HttpGet]
    public async Task<IActionResult> OrganizationDetails(string orgId)
    {
        if (string.IsNullOrWhiteSpace(orgId))
            return NotFound();

        var organizations = await _superAdminSvc.ListOrganizationsAsync();
        var organization = organizations.FirstOrDefault(x =>
            string.Equals(x.OrganizationId, orgId, StringComparison.OrdinalIgnoreCase));

        if (organization is null)
            return NotFound();

        var deletedOrgRetentionDays = _configuration.GetValue<int>("DeletedOrgRetentionDays", 14);
        var users = await _orgAdminSvc.ListUsersAsync(orgId);

        var model = new AdminOrganizationDetailsViewModel
        {
            Organization = organization,
            DeletedOrgRetentionDays = deletedOrgRetentionDays,
            Users = users
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AllBatches(int page = 1, int pageSize = 50, string sort = "created", string dir = "desc", string? orgId = null)
    {
        const int minPageSize = 10;
        const int maxPageSize = 200;
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, minPageSize, maxPageSize);
        var normalizedSort = sort.ToLowerInvariant() switch
        {
            "org" => "org",
            "name" => "name",
            "status" => "status",
            "created" => "created",
            "validated" => "validated",
            "files" => "files",
            _ => "created"
        };
        var normalizedDir = dir.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

        var allBatches = await _dataSvc.GetAllBatchesAcrossOrgsAsync();
        var selectedOrgId = string.IsNullOrWhiteSpace(orgId) ? null : orgId.Trim();
        var orgOptions = allBatches
            .Where(b => !string.IsNullOrWhiteSpace(b.OrganizationId) && !string.IsNullOrWhiteSpace(b.OrganizationName))
            .GroupBy(b => b.OrganizationId)
            .Select(g => new AdminOrgFilterOption
            {
                Id = g.Key,
                Name = g.Select(x => x.OrganizationName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? g.Key
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(selectedOrgId))
        {
            allBatches = allBatches
                .Where(b => string.Equals(b.OrganizationId, selectedOrgId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        static int StatusOrder(BatchDetail b) =>
            b.MarkedForDeletionAtUtc.HasValue ? 2 :
            b.LockedByUserId != null ? 1 : 0;

        IEnumerable<BatchDetail> ordered = normalizedSort switch
        {
            "org" when normalizedDir == "asc" => allBatches
                .OrderBy(b => b.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(b => b.CreatedAtUtc),
            "org" => allBatches
                .OrderByDescending(b => b.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(b => b.CreatedAtUtc),
            "name" when normalizedDir == "asc" => allBatches
                .OrderBy(b => b.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(b => b.CreatedAtUtc),
            "name" => allBatches
                .OrderByDescending(b => b.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(b => b.CreatedAtUtc),
            "status" when normalizedDir == "asc" => allBatches
                .OrderBy(StatusOrder)
                .ThenByDescending(b => b.CreatedAtUtc),
            "status" => allBatches
                .OrderByDescending(StatusOrder)
                .ThenByDescending(b => b.CreatedAtUtc),
            "validated" when normalizedDir == "asc" => allBatches
                .OrderBy(b => b.ValidatedCount)
                .ThenByDescending(b => b.CreatedAtUtc),
            "validated" => allBatches
                .OrderByDescending(b => b.ValidatedCount)
                .ThenByDescending(b => b.CreatedAtUtc),
            "files" when normalizedDir == "asc" => allBatches
                .OrderBy(b => b.FileCount)
                .ThenByDescending(b => b.CreatedAtUtc),
            "files" => allBatches
                .OrderByDescending(b => b.FileCount)
                .ThenByDescending(b => b.CreatedAtUtc),
            "created" when normalizedDir == "asc" => allBatches
                .OrderBy(b => b.CreatedAtUtc),
            _ => allBatches
                .OrderByDescending(b => b.CreatedAtUtc)
        };

        var orderedArray = ordered.ToArray();

        var totalCount = orderedArray.Length;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var paged = orderedArray
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var model = new AdminAllBatchesViewModel
        {
            Batches = paged,
            OrgOptions = orgOptions,
            SelectedOrgId = selectedOrgId,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Sort = normalizedSort,
            Dir = normalizedDir
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AllUsers(int page = 1, int pageSize = 50, string sort = "name", string dir = "asc", string? orgId = null)
    {
        const int minPageSize = 10;
        const int maxPageSize = 200;
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, minPageSize, maxPageSize);

        var normalizedSort = sort.Equals("org", StringComparison.OrdinalIgnoreCase) ? "org" : "name";
        var normalizedDir = dir.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";

        var users = await _dataSvc.GetAllUsersAsync();
        var selectedOrgId = string.IsNullOrWhiteSpace(orgId) ? null : orgId.Trim();
        var orgOptions = users
            .Where(u => !string.IsNullOrWhiteSpace(u.OrganizationId) && !string.IsNullOrWhiteSpace(u.OrganizationName))
            .GroupBy(u => u.OrganizationId!)
            .Select(g => new AdminOrgFilterOption
            {
                Id = g.Key,
                Name = g.Select(x => x.OrganizationName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? g.Key
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(selectedOrgId))
        {
            users = users
                .Where(u => string.Equals(u.OrganizationId, selectedOrgId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        IEnumerable<SuperAdminUserDetail> ordered = normalizedSort switch
        {
            "org" when normalizedDir == "desc" => users
                .OrderByDescending(u => u.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, StringComparer.OrdinalIgnoreCase),
            "org" => users
                .OrderBy(u => u.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, StringComparer.OrdinalIgnoreCase),
            _ when normalizedDir == "desc" => users
                .OrderByDescending(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.OrganizationName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
        };

        var orderedArray = ordered.ToArray();
        var totalCount = orderedArray.Length;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var paged = orderedArray
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var model = new AdminAllUsersViewModel
        {
            Users = paged,
            OrgOptions = orgOptions,
            SelectedOrgId = selectedOrgId,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Sort = normalizedSort,
            Dir = normalizedDir
        };

        return View(model);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _superAdminSvc.CreateOrganizationAsync(request, baseUrl);
            return Json(new
            {
                success = true,
                orgId = result.OrganizationId,
                adminEmail = result.AdminEmail,
                setupLink = result.SetupLink,
                emailSent = result.EmailSent,
                stripeProvisioned = result.StripeProvisioned,
                stripeProvisioningError = result.StripeProvisioningError
            });
        }
        catch (DuplicateOrganizationNameException ex)
        {
            _logger.LogWarning(ex, "CreateOrganization rejected duplicate org name {Name}", request.Name);
            return Json(new
            {
                success = false,
                duplicateName = true,
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateOrganization failed for {Name}", request.Name);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RevokeOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.RevokeOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RevokeOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReEnableOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.ReEnableOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReEnableOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.MarkOrganizationForDeletionAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkOrganizationForDeletion failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReinstateOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.ReinstateMarkedOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReinstateOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> HardDeleteOrganization([FromBody] OrgIdRequest request)
    {
        try
        {
            await _superAdminSvc.DeleteOrganizationAsync(request.OrgId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HardDeleteOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetUserPassword([FromBody] SetUserPasswordRequest request)
    {
        try
        {
            var user = await GetUserManagerAsync(request.UserId);
            if (user == null)
                return Json(new { success = false, error = "User not found." });

            var token = await user.UserManager.GeneratePasswordResetTokenAsync(user.User);
            var result = await user.UserManager.ResetPasswordAsync(user.User, token, request.NewPassword);
            if (!result.Succeeded)
                return Json(new { success = false, error = string.Join("; ", result.Errors.Select(e => e.Description)) });

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetUserPassword failed for {UserId}", request.UserId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReinviteUser([FromBody] ReinviteUserRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
                return Json(new { success = false, error = "UserId is required." });

            var user = await GetUserManagerAsync(request.UserId);
            if (user == null)
                return Json(new { success = false, error = "User not found." });

            if (string.IsNullOrWhiteSpace(user.User.Email))
                return Json(new { success = false, error = "User does not have an email address." });

            var authSvc = HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var emailSvc = HttpContext.RequestServices.GetRequiredService<IEmailService>();
            var email = user.User.Email;
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { success = false, error = "User does not have an email address." });
            var nonNullEmail = email.Trim();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var resetLink = await authSvc.GeneratePasswordResetLinkAsync(nonNullEmail, baseUrl);
            if (resetLink is null)
                return Json(new { success = false, error = "Unable to generate password reset link." });

            var displayName = user.User.FullName ?? nonNullEmail;
            await emailSvc.SendPasswordResetAsync(nonNullEmail, displayName, resetLink);

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReinviteUser failed for {UserId}", request.UserId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllBatches()
    {
        var batches = await _dataSvc.GetAllBatchesAcrossOrgsAsync();
        return Json(batches);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ProvisionStripe([FromBody] ProvisionStripeRequest request)
    {
        try
        {
            await _superAdminSvc.ProvisionStripeAsync(request.OrgId, string.IsNullOrWhiteSpace(request.PriceId) ? null : request.PriceId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProvisionStripe failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SyncStripeStatus([FromBody] SyncStripeStatusRequest request)
    {
        try
        {
            var result = await _superAdminSvc.SyncStripeStatusAsync(request.OrgId);
            return Json(new { success = true, status = result.IsApplicable ? result.Status : "not-billed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncStripeStatus failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdateBetaMaxOcrPages([FromBody] UpdateBetaMaxOcrPagesRequest request)
    {
        try
        {
            await _superAdminSvc.UpdateBetaMaxOcrPagesAsync(request.OrgId, request.MaxOcrPages);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateBetaMaxOcrPages failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PromoteOrganization([FromBody] PromoteOrganizationRequest request)
    {
        try
        {
            var result = await _superAdminSvc.PromoteOrganizationAsync(request.OrgId, request.NewOrganizationName);
            return Json(new
            {
                success = true,
                organizationName = result.OrganizationName,
                stripeProvisioned = result.StripeProvisioned,
                stripeProvisioningError = result.StripeProvisioningError
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromoteOrganization failed for {OrgId}", request.OrgId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> OcrQueue()
    {
        // Build org ID → name lookup from existing service.
        var orgs = await _superAdminSvc.ListOrganizationsAsync();
        var orgNames = orgs.ToDictionary(o => o.OrganizationId, o => o.Name);

        var vm = new OcrQueueViewModel
        {
            TotalQueued     = _ocrJobQueue.TotalCount,
            PerOrgCount     = _ocrJobQueue.CountPerOrg,
            PerOrgInvoices  = _ocrJobQueue.InvoicesPerOrg,
            OrgNames        = orgNames,
            IsPaused        = _ocrQueueProcessor.IsPaused,
        };

        return View(vm);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PauseQueue()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _ocrQueueProcessor.PauseAndDrainAsync(cts.Token);
            return Json(new { success = true, isPaused = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PauseQueue failed.");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult ResumeQueue()
    {
        _ocrQueueProcessor.Resume();
        return Json(new { success = true, isPaused = false });
    }

    // Helper — resolves lazily to avoid circular ctor dependency
    private Task<UserManagerContext?> GetUserManagerAsync(string userId)
    {
        var userManager = HttpContext.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        return ResolveUser(userManager, userId);
    }

    private static async Task<UserManagerContext?> ResolveUser(
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> mgr, string userId)
    {
        var user = await mgr.FindByIdAsync(userId);
        return user is null ? null : new UserManagerContext(mgr, user);
    }

    private sealed record UserManagerContext(
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> UserManager,
        ApplicationUser User);
}

// Local request DTOs
public sealed class OrgIdRequest
{
    public string OrgId { get; set; } = string.Empty;
}

public sealed class SetUserPasswordRequest
{
    public string UserId { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ReinviteUserRequest
{
    public string UserId { get; set; } = string.Empty;
}

public sealed class UpdateBetaMaxOcrPagesRequest
{
    public string OrgId { get; set; } = string.Empty;
    public int MaxOcrPages { get; set; }
}

public sealed class PromoteOrganizationRequest
{
    public string OrgId { get; set; } = string.Empty;
    public string? NewOrganizationName { get; set; }
}
