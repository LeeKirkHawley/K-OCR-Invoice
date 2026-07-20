using K_OCRLib.Data;
using K_OCRLib.Security;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCRLib.Services;

public class BatchNotificationService : IBatchNotificationService
{
    private readonly ApplicationDbContext _appDb;
    private readonly IEmailService _emailService;
    private readonly IPathService _pathService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BatchNotificationService> _logger;

    public BatchNotificationService(
        ApplicationDbContext appDb,
        IEmailService emailService,
        IPathService pathService,
        IConfiguration configuration,
        ILogger<BatchNotificationService> logger)
    {
        _appDb         = appDb;
        _emailService  = emailService;
        _pathService   = pathService;
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task NotifyBatchSoftDeletedAsync(string orgId, string batchName, CancellationToken ct = default)
    {
        var retentionDays = _configuration.GetValue<int>("DeletedBatchRetentionDays", 14);

        var admins = await _appDb.UserOrganizationMemberships
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.OrganizationId == orgId && m.Role == RoleNames.OrganizationAdmin)
            .Select(m => new { m.User.Email, m.User.FullName })
            .ToListAsync(ct);

        var org = await _appDb.Organizations
            .AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        var orgName = org ?? orgId;

        foreach (var admin in admins)
        {
            if (string.IsNullOrWhiteSpace(admin.Email))
                continue;
            try
            {
                await _emailService.SendBatchSoftDeletedNotificationAsync(
                    admin.Email,
                    admin.FullName ?? admin.Email,
                    orgName,
                    batchName,
                    retentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send batch soft-delete notification to {Email} for batch '{Batch}' in org {OrgId}.",
                    admin.Email, batchName, orgId);
            }
        }
    }

    public async Task NotifyBatchHardDeletedAsync(string orgSanitizedName, string batchName, CancellationToken ct = default)
    {
        var allOrgs = await _appDb.Organizations
            .Include(o => o.UserMemberships)
                .ThenInclude(m => m.User)
            .AsNoTracking()
            .ToListAsync(ct);

        var org = allOrgs.FirstOrDefault(o =>
            string.Equals(_pathService.SanitizeName(o.Name), orgSanitizedName, StringComparison.OrdinalIgnoreCase));

        if (org is null)
        {
            _logger.LogWarning(
                "Could not find org for sanitized folder name '{OrgSanitizedName}'; skipping hard-delete notification for batch '{Batch}'.",
                orgSanitizedName, batchName);
            return;
        }

        foreach (var admin in org.UserMemberships
            .Where(m => m.Role == RoleNames.OrganizationAdmin)
            .Select(m => m.User))
        {
            if (string.IsNullOrWhiteSpace(admin.Email))
                continue;
            try
            {
                await _emailService.SendBatchHardDeletedNotificationAsync(
                    admin.Email,
                    admin.FullName ?? admin.Email,
                    org.Name,
                    batchName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send batch hard-delete notification to {Email} for batch '{Batch}' in org '{OrgName}'.",
                    admin.Email, batchName, org.Name);
            }
        }
    }

    public async Task NotifyBatchRestoredAsync(string orgId, string batchName, CancellationToken ct = default)
    {
        var admins = await _appDb.UserOrganizationMemberships
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.OrganizationId == orgId && m.Role == RoleNames.OrganizationAdmin)
            .Select(m => new { m.User.Email, m.User.FullName })
            .ToListAsync(ct);

        var org = await _appDb.Organizations
            .AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        var orgName = org ?? orgId;

        foreach (var admin in admins)
        {
            if (string.IsNullOrWhiteSpace(admin.Email))
                continue;
            try
            {
                await _emailService.SendBatchRestoredNotificationAsync(
                    admin.Email,
                    admin.FullName ?? admin.Email,
                    orgName,
                    batchName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send batch restore notification to {Email} for batch '{Batch}' in org {OrgId}.",
                    admin.Email, batchName, orgId);
            }
        }
    }
}
