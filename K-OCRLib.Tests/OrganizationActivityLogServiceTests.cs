using K_OCR.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class OrganizationActivityLogServiceTests
{
    [Fact]
    public async Task LogBatchCreatedAsync_RollsDailyAndKeepsSevenFiles()
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
            var now = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

            var service = new OrganizationActivityLogService(
                pathService,
                Mock.Of<ILogger<OrganizationActivityLogService>>(),
                () => now);

            const string orgName = "Acme Org";

            for (var i = 0; i < 8; i++)
            {
                now = new DateTime(2026, 5, 20 + i, 12, 0, 0, DateTimeKind.Utc);
                await service.LogBatchCreatedAsync(orgName, $"Batch-{i}", "User");
            }

            var orgFolder = pathService.GetOrgFolderPath(orgName);
            var files = Directory.GetFiles(orgFolder, "activity-*.log");

            Assert.Equal(7, files.Length);
            Assert.DoesNotContain(files, f => f.Contains("20260520"));
            Assert.Contains(files, f => f.Contains("20260527"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
