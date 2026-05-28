using K_OCR.Services;
using Microsoft.Extensions.Hosting;

namespace KOCRAsp.Services;

public sealed class GuestAccountCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuestAccountCleanupService> _logger;

    public GuestAccountCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<GuestAccountCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
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
        var guestCleanupService = scope.ServiceProvider.GetRequiredService<IGuestCleanupService>();
        return await guestCleanupService.RunCleanupCycleAsync(cancellationToken);
    }

}

