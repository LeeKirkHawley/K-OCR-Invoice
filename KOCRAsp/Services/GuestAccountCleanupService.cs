using Microsoft.Extensions.Hosting;

namespace KOCRAsp.Services;

public sealed class GuestAccountCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GuestAccountCleanupService> _logger;

    public GuestAccountCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<GuestAccountCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCleanupCycleAsync(stoppingToken);
    }

    public async Task<int> RunCleanupCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var superAdminService = scope.ServiceProvider.GetRequiredService<ISuperAdminService>();

        var markedCount = 0;
        var retentionDays = _configuration.GetValue<int>("GuestAccountRetentionDays");
        if (retentionDays > 0)
        {
            markedCount = await superAdminService.CleanupExpiredGuestAccountsAsync(
                TimeSpan.FromDays(retentionDays),
                cancellationToken);

            if (markedCount > 0)
                _logger.LogInformation(
                    "Marked {Count} expired guest account(s) for deletion (retention: {Days} day(s)).",
                    markedCount, retentionDays);
        }

        var hardDeletedCount = 0;
        var softDeleteRetentionDays = _configuration.GetValue<int>("DeletedOrgRetentionDays");
        if (softDeleteRetentionDays > 0)
        {
            hardDeletedCount = await superAdminService.CleanupExpiredSoftDeletesAsync(
                TimeSpan.FromDays(softDeleteRetentionDays),
                cancellationToken);

            if (hardDeletedCount > 0)
                _logger.LogInformation(
                    "Hard-deleted {Count} soft-deleted org(s) (retention: {Days} day(s)).",
                    hardDeletedCount, softDeleteRetentionDays);
        }

        return markedCount + hardDeletedCount;
    }
}
