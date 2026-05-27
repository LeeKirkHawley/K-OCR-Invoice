using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Services;

public class BatchCleanupServiceTests
{
    [Fact]
    public async Task CleanupExpiredBatchesAsync_WritesHardDeletedBatchAction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var orgName = "Acme Org";
            var orgFolderName = "Acme_Org";
            var batchName = "Batch1";

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Kocr:BaseDirectory"] = tempRoot
                })
                .Build();

            var pathService = new PathService(configuration);
            var orgFolder = pathService.GetOrgFolderPath(orgName);
            Directory.CreateDirectory(orgFolder);
            var dbPath = pathService.GetOrgDbPath(orgName);

            var connectionString = $"Data Source={dbPath}";
            var options = new DbContextOptionsBuilder<KOCRDbContext>()
                .UseSqlite(connectionString)
                .Options;

            await using (var seedContext = new KOCRDbContext(options))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Batches.Add(new Batch
                {
                    Name = batchName,
                    BatchNumber = 1,
                    FolderPath = Path.Combine(orgFolder, "Batch1"),
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                    CreatedByUserId = "user-1",
                    MarkedForDeletionAtUtc = DateTime.UtcNow.AddDays(-15)
                });
                await seedContext.SaveChangesAsync();
            }

            var cleanupService = new BatchCleanupService(
                pathService,
                new DatabaseSettings(),
                Mock.Of<IBatchNotificationService>(),
                new OrganizationActivityLogService(pathService, Mock.Of<ILogger<OrganizationActivityLogService>>()),
                Mock.Of<ILogger<BatchCleanupService>>());

            var deletedCount = await cleanupService.CleanupExpiredBatchesAsync(TimeSpan.FromDays(14));

            await using (var verifyContext = new KOCRDbContext(options))
            {
                var actions = await verifyContext.BatchActions.ToListAsync();

                Assert.Equal(1, deletedCount);
                Assert.Empty(await verifyContext.Batches.ToListAsync());
                Assert.Single(actions);
                Assert.Equal(BatchActionTypes.HardDeleted, actions[0].Action);
                Assert.Equal(batchName, actions[0].BatchName);
                Assert.Equal(orgFolderName, actions[0].Organization);
                Assert.Equal("System", actions[0].OrgUser);

                var logPath = Path.Combine(orgFolder, $"activity-{DateTime.UtcNow:yyyyMMdd}.log");
                Assert.True(File.Exists(logPath));
                var logText = await File.ReadAllTextAsync(logPath);
                Assert.Contains("BatchDeleted", logText);
                Assert.Contains(batchName, logText);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
