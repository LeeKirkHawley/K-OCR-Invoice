using K_OCRLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRIntegrationTests;

/// <summary>
/// Real end-to-end coverage for OrganizationActivityLogService: real PathService,
/// real filesystem writes, no mocks for the thing under test. A mocked ILogger is
/// used only to verify the warning path, showing how mocks slot in when needed.
/// </summary>
public class OrganizationActivityLogServiceIntegrationTests
{
    [Fact]
    public async Task LogBatchCreatedAsync_WritesRealDailyLogFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Kocr:BaseDirectory"] = tempRoot
                })
                .Build();

            var pathService = new PathService(config);
            var now = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

            var service = new OrganizationActivityLogService(
                pathService,
                Mock.Of<ILogger<OrganizationActivityLogService>>(),
                () => now);

            await service.LogBatchCreatedAsync("Acme Org", "Batch-1", "alice@example.com");

            var orgFolder = pathService.GetOrgFolderPath("Acme Org");
            var logFile = Path.Combine(orgFolder, "activity-20260601.log");

            Assert.True(File.Exists(logFile));
            var contents = await File.ReadAllTextAsync(logFile);
            Assert.Contains("BatchCreated", contents);
            Assert.Contains("Batch-1", contents);
            Assert.Contains("alice@example.com", contents);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LogBatchCreatedAsync_WithEmptyOrgName_LogsWarningAndSkipsWrite()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Kocr:BaseDirectory"] = tempRoot
                })
                .Build();

            var pathService = new PathService(config);
            var logger = new Mock<ILogger<OrganizationActivityLogService>>();

            var service = new OrganizationActivityLogService(pathService, logger.Object);

            await service.LogBatchCreatedAsync(string.Empty, "Batch-1", "alice@example.com");

            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            Assert.Empty(Directory.GetFiles(tempRoot, "activity-*.log", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
