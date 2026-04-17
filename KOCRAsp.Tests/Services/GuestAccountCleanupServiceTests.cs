using KOCRAsp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Services;

public class GuestAccountCleanupServiceTests
{
    [Fact]
    public async Task RunCleanupCycleAsync_MarksExpiredGuests_WhenRetentionIsConfigured()
    {
        var superAdminService = new Mock<ISuperAdminService>();
        superAdminService
            .Setup(service => service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromDays(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        using var provider = new ServiceCollection()
            .AddScoped(_ => superAdminService.Object)
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionDays"] = "15"
            })
            .Build();

        var cleanupService = new GuestAccountCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<ILogger<GuestAccountCleanupService>>());

        var markedCount = await cleanupService.RunCleanupCycleAsync(CancellationToken.None);

        Assert.Equal(2, markedCount);
        superAdminService.Verify(
            service => service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromDays(15), It.IsAny<CancellationToken>()),
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
                ["GuestAccountRetentionDays"] = "0"
            })
            .Build();

        var cleanupService = new GuestAccountCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<ILogger<GuestAccountCleanupService>>());

        var count = await cleanupService.RunCleanupCycleAsync(CancellationToken.None);

        Assert.Equal(0, count);
        superAdminService.Verify(
            service => service.CleanupExpiredGuestAccountsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
