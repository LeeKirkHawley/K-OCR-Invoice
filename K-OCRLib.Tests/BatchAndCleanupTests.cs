using K_OCRLib.Configuration;
using K_OCRLib.Data;
using K_OCRLib.Identity;
using K_OCRLib.Models;
using K_OCRLib.Security;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class BatchAndCleanupTests
{
    [Fact]
    public async Task ReportingService_RecordBatchOcrEventAsync_PersistsReport()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reporting-{Guid.NewGuid()}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ReportingDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var db = new ReportingDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var service = new ReportingService(db, Mock.Of<ILogger<ReportingService>>());
            await service.RecordBatchOcrEventAsync(new BatchOcrReportRequest
            {
                OrganizationId = "org-1",
                OrganizationName = "Acme",
                BatchName = "Batch 1",
                Invoices =
                [
                    new OcrInvoiceResult { FileName = "a.pdf", OcrSucceeded = true, OcrService = "Azure", PageCount = 2 },
                    new OcrInvoiceResult { FileName = "b.pdf", OcrSucceeded = false, OcrService = "Tesseract", PageCount = 1 }
                ]
            });

            await using var verify = new ReportingDbContext(options);
            var report = await verify.OcrBatchReports.Include(r => r.Items).SingleAsync();

            Assert.Equal("Acme", report.OrganizationName);
            Assert.Equal(2, report.Items.Count);
        }
        finally
        {
            ClearTempDb(dbPath);
        }
    }

    [Fact]
    public async Task BatchNotificationService_NotifyBatchSoftDeletedAsync_SendsToAdmins()
    {
        var (db, dbPath, root) = await CreateApplicationDbAsync();
        try
        {
            var org = new Organization { Id = Guid.NewGuid().ToString(), Name = "Acme Org" };
            var admin = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin@acme.test",
                Email = "admin@acme.test",
                FullName = "Admin",
                OrganizationId = org.Id
            };
            var regular = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "user@acme.test",
                Email = "user@acme.test",
                FullName = "User",
                OrganizationId = org.Id
            };
            db.Organizations.Add(org);
            db.Users.AddRange(admin, regular);
            db.UserOrganizationMemberships.AddRange(
                new UserOrganizationMembership
                {
                    UserId = admin.Id,
                    OrganizationId = org.Id,
                    Role = RoleNames.OrganizationAdmin
                },
                new UserOrganizationMembership
                {
                    UserId = regular.Id,
                    OrganizationId = org.Id,
                    Role = RoleNames.OrganizationUser
                });
            await db.SaveChangesAsync();

            var email = new Mock<IEmailService>();
            var path = new PathService(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Kocr:BaseDirectory"] = root })
                .Build());
            var service = new BatchNotificationService(
                db,
                email.Object,
                path,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeletedBatchRetentionDays"] = "7" }).Build(),
                Mock.Of<ILogger<BatchNotificationService>>());

            await service.NotifyBatchSoftDeletedAsync(org.Id, "Batch 1");

            email.Verify(x => x.SendBatchSoftDeletedNotificationAsync(
                "admin@acme.test",
                "Admin",
                "Acme Org",
                "Batch 1",
                7), Times.Once);
            email.VerifyNoOtherCalls();
        }
        finally
        {
            await db.DisposeAsync();
            CleanupDirectory(root);
            ClearTempDb(dbPath);
        }
    }

    [Fact]
    public async Task BatchService_GetNextBatchNumberAsync_ReturnsMaxPlusOne()
    {
        var (factory, dbPath) = CreateBatchDb();
        try
        {
            await using (var seed = factory.CreateDbContext())
            {
                seed.Database.Migrate();
                seed.Batches.AddRange(
                    new Batch { Name = "B1", BatchNumber = 1, FolderPath = "/tmp/b1", CreatedByUserId = "u", CreatedAtUtc = DateTime.UtcNow },
                    new Batch { Name = "B2", BatchNumber = 3, FolderPath = "/tmp/b2", CreatedByUserId = "u", CreatedAtUtc = DateTime.UtcNow });
                await seed.SaveChangesAsync();
            }

            var service = new BatchService(
                factory,
                Mock.Of<IPathService>(),
                Mock.Of<IInvoiceProcessingService>(),
                Mock.Of<IFileService>(),
                Mock.Of<ITenantContext>(),
                Mock.Of<IReportingService>(),
                Mock.Of<IStripeUsageService>(),
                Mock.Of<IOcrEnqueueService>(),
                Mock.Of<ILogger<BatchService>>());

            var next = await service.GetNextBatchNumberAsync("org");
            Assert.Equal(4, next);
        }
        finally
        {
            ClearTempDb(dbPath);
        }
    }

    [Fact]
    public async Task BatchService_DeleteBatchAsync_SoftDeletesBatch()
    {
        var (factory, dbPath) = CreateBatchDb();
        try
        {
            int batchId;
            await using (var seed = factory.CreateDbContext())
            {
                seed.Database.Migrate();
                var batch = new Batch
                {
                    Name = "B1",
                    BatchNumber = 1,
                    FolderPath = "/tmp/b1",
                    CreatedByUserId = "u",
                    CreatedAtUtc = DateTime.UtcNow
                };
                seed.Batches.Add(batch);
                await seed.SaveChangesAsync();
                batchId = batch.BatchId;
            }

            var service = new BatchService(
                factory,
                Mock.Of<IPathService>(),
                Mock.Of<IInvoiceProcessingService>(),
                Mock.Of<IFileService>(),
                Mock.Of<ITenantContext>(),
                Mock.Of<IReportingService>(),
                Mock.Of<IStripeUsageService>(),
                Mock.Of<IOcrEnqueueService>(),
                Mock.Of<ILogger<BatchService>>());

            var name = await service.DeleteBatchAsync(batchId, "u");

            await using var verify = factory.CreateDbContext();
            var deletedBatch = await verify.Batches.FindAsync(batchId);

            Assert.Equal("B1", name);
            Assert.NotNull(deletedBatch!.MarkedForDeletionAtUtc);
        }
        finally
        {
            ClearTempDb(dbPath);
        }
    }

    [Fact]
    public async Task BatchCleanupService_CleanupExpiredBatchesAsync_WritesHardDeletedAction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Kocr:BaseDirectory"] = tempRoot
                })
                .Build();

            var pathService = new PathService(configuration);
            var orgName = "Acme Org";
            var orgFolder = pathService.GetOrgFolderPath(orgName);
            Directory.CreateDirectory(orgFolder);
            var dbPath = pathService.GetOrgDbPath(orgName);

            var options = new DbContextOptionsBuilder<KOCRDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var seed = new KOCRDbContext(options))
            {
                await seed.Database.MigrateAsync();
                seed.Batches.Add(new Batch
                {
                    Name = "Batch1",
                    BatchNumber = 1,
                    FolderPath = Path.Combine(orgFolder, "Batch1"),
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                    CreatedByUserId = "user-1",
                    MarkedForDeletionAtUtc = DateTime.UtcNow.AddDays(-15)
                });
                await seed.SaveChangesAsync();
            }

            var cleanup = new BatchCleanupService(
                pathService,
                new DatabaseSettings(),
                Mock.Of<IBatchNotificationService>(),
                new OrganizationActivityLogService(pathService, Mock.Of<ILogger<OrganizationActivityLogService>>()),
                Mock.Of<ILogger<BatchCleanupService>>());

            var deletedCount = await cleanup.CleanupExpiredBatchesAsync(TimeSpan.FromDays(14));
            await using var verify = new KOCRDbContext(options);

            Assert.Equal(1, deletedCount);
            Assert.Empty(await verify.Batches.ToListAsync());
            Assert.Single(await verify.BatchActions.ToListAsync());

            var logPath = Path.Combine(orgFolder, $"activity-{DateTime.UtcNow:yyyyMMdd}.log");
            Assert.True(File.Exists(logPath));
            Assert.Contains("BatchDeleted", await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanupDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GuestCleanupService_RunCleanupCycleAsync_InvokesConfiguredDependencies()
    {
        var super = new Mock<ISuperAdminService>();
        super.Setup(s => s.CleanupExpiredGuestAccountsAsync(TimeSpan.FromDays(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new[] { "Guest1" });
        super.Setup(s => s.CleanupExpiredSoftDeletesAsync(TimeSpan.FromDays(14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var batchCleanup = new Mock<IBatchCleanupService>();
        batchCleanup.Setup(s => s.CleanupExpiredBatchesAsync(TimeSpan.FromDays(14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GuestAccountRetentionDays"] = "15",
                ["DeletedOrgRetentionDays"] = "14",
                ["DeletedBatchRetentionDays"] = "14"
            })
            .Build();

        var service = new GuestCleanupService(super.Object, batchCleanup.Object, config, Mock.Of<ILogger<GuestCleanupService>>());

        var count = await service.RunCleanupCycleAsync();

        Assert.Equal(4, count);
    }

    private static async Task<(ApplicationDbContext db, string dbPath, string root)> CreateApplicationDbAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"appdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "app.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, dbPath, root);
    }

    private static (DirectDbContextFactory factory, string dbPath) CreateBatchDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}.db");
        return (new DirectDbContextFactory(dbPath), dbPath);
    }

    private static void ClearTempDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    private static void CleanupDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
