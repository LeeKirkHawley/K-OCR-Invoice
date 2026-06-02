using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCRLib.Services;

public class GuestCleanupService : IGuestCleanupService
{
    private readonly ISuperAdminService _superAdminSvc;
    private readonly IBatchCleanupService _batchCleanupSvc;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GuestCleanupService> _logger;

    public GuestCleanupService(
        ISuperAdminService superAdminSvc,
        IBatchCleanupService batchCleanupSvc,
        IConfiguration configuration,
        ILogger<GuestCleanupService> logger)
    {
        _superAdminSvc   = superAdminSvc;
        _batchCleanupSvc = batchCleanupSvc;
        _configuration   = configuration;
        _logger          = logger;
    }

    public async Task<int> RunCleanupCycleAsync(CancellationToken cancellationToken = default)
    {
        var markedCount = 0;
        var retentionDays = _configuration.GetValue<int>("GuestAccountRetentionDays");
        if (retentionDays > 0)
        {
            var markedAccounts = await _superAdminSvc.CleanupExpiredGuestAccountsAsync(
                TimeSpan.FromDays(retentionDays),
                cancellationToken);
            markedCount = markedAccounts.Count;

            if (markedCount > 0)
                _logger.LogInformation(
                    "Marked {Count} expired guest account(s) for deletion (retention: {Days} day(s)): {Accounts}",
                    markedCount, retentionDays, string.Join(", ", markedAccounts));
        }

        var hardDeletedOrgCount = 0;
        var softDeleteRetentionDays = _configuration.GetValue<int>("DeletedOrgRetentionDays");
        if (softDeleteRetentionDays > 0)
        {
            hardDeletedOrgCount = await _superAdminSvc.CleanupExpiredSoftDeletesAsync(
                TimeSpan.FromDays(softDeleteRetentionDays),
                cancellationToken);

            if (hardDeletedOrgCount > 0)
                _logger.LogInformation(
                    "Hard-deleted {Count} soft-deleted org(s) (retention: {Days} day(s)).",
                    hardDeletedOrgCount, softDeleteRetentionDays);
        }

        var hardDeletedBatchCount = 0;
        var batchRetentionDays = _configuration.GetValue<int>("DeletedBatchRetentionDays");
        if (batchRetentionDays > 0)
        {
            hardDeletedBatchCount = await _batchCleanupSvc.CleanupExpiredBatchesAsync(
                TimeSpan.FromDays(batchRetentionDays),
                cancellationToken);

            if (hardDeletedBatchCount > 0)
                _logger.LogInformation(
                    "Hard-deleted {Count} soft-deleted batch(es) (retention: {Days} day(s)).",
                    hardDeletedBatchCount, batchRetentionDays);
        }

        return markedCount + hardDeletedOrgCount + hardDeletedBatchCount;
    }
}
