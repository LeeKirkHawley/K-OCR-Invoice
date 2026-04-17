using Microsoft.Extensions.Hosting;

namespace KOCRAsp.Services;

public sealed class GuestAccountCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

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
        var retentionMinutes = _configuration.GetValue<int>("GuestAccountRetentionMinutes");
        if (retentionMinutes <= 0)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var superAdminService = scope.ServiceProvider.GetRequiredService<ISuperAdminService>();
        var deletedCount = await superAdminService.CleanupExpiredGuestAccountsAsync(
            TimeSpan.FromMinutes(retentionMinutes),
            cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Deleted {DeletedCount} expired guest account(s) using retention period of {RetentionMinutes} minute(s).",
                deletedCount,
                retentionMinutes);
        }

        return deletedCount;
    }
}
