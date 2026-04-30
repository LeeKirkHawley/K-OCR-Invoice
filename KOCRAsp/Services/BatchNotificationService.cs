using K_OCR.Services;
using KOCRAsp.Data;
using Microsoft.EntityFrameworkCore;

namespace KOCRAsp.Services;

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
        var retentionDays = _configuration.GetValue<int>("DeletedBatchRetentionDays");

        var admins = await _appDb.Users
            .Where(u => u.OrganizationId == orgId && u.IsOrganizationAdmin)
            .Select(u => new { u.Email, u.FullName })
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
        // Find the org whose display name sanitizes to the given folder name
        var allOrgs = await _appDb.Organizations
            .Include(o => o.Users)
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

        foreach (var admin in org.Users.Where(u => u.IsOrganizationAdmin))
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
}
