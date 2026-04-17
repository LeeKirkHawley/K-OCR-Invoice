using KOCRAsp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Services;

public class GuestAccountCleanupServiceTests
{
    [Fact]
    public async Task RunCleanupCycleAsync_DeletesExpiredGuests_WhenRetentionIsConfigured()
    {
        var superAdminService = new Mock<ISuperAdminService>();
        superAdminService
            .Setup(service => service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        using var provider = new ServiceCollection()
            .AddScoped(_ => superAdminService.Object)
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionMinutes"] = "15"
            })
            .Build();

        var cleanupService = new GuestAccountCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<ILogger<GuestAccountCleanupService>>());

        var deletedCount = await cleanupService.RunCleanupCycleAsync(CancellationToken.None);

        Assert.Equal(2, deletedCount);
        superAdminService.Verify(
            service => service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCleanupCycleAsync_DoesNothing_WhenRetentionIsDisabled()
    {
        var superAdminService = new Mock<ISuperAdminService>();

        using var provider = new ServiceCollection()
            .AddScoped(_ => superAdminService.Object)
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionMinutes"] = "0"
            })
            .Build();

        var cleanupService = new GuestAccountCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<ILogger<GuestAccountCleanupService>>());

        var deletedCount = await cleanupService.RunCleanupCycleAsync(CancellationToken.None);

        Assert.Equal(0, deletedCount);
        superAdminService.Verify(
            service => service.CleanupExpiredGuestAccountsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
