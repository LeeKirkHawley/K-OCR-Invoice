using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
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
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { "Guest-1", "Guest-2" });
        superAdminService
            .Setup(service => service.CleanupExpiredSoftDeletesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var batchCleanupService = new Mock<IBatchCleanupService>();
        batchCleanupService
            .Setup(service => service.CleanupExpiredBatchesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionDays"] = "15",
                ["DeletedOrgRetentionDays"] = "14",
                ["DeletedBatchRetentionDays"] = "14"
            })
            .Build();

        var cleanupService = new GuestCleanupService(
            superAdminService.Object,
            batchCleanupService.Object,
            configuration,
            Mock.Of<ILogger<GuestCleanupService>>());

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
        var batchCleanupService = new Mock<IBatchCleanupService>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionDays"] = "0",
                ["DeletedOrgRetentionDays"] = "0",
                ["DeletedBatchRetentionDays"] = "0"
            })
            .Build();

        var cleanupService = new GuestCleanupService(
            superAdminService.Object,
            batchCleanupService.Object,
            configuration,
            Mock.Of<ILogger<GuestCleanupService>>());

        var count = await cleanupService.RunCleanupCycleAsync(CancellationToken.None);

        Assert.Equal(0, count);
        superAdminService.Verify(
            service => service.CleanupExpiredGuestAccountsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        batchCleanupService.Verify(
            service => service.CleanupExpiredBatchesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
