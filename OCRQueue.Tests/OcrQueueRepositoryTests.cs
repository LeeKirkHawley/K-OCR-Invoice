using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OCRQueue.Models;
using OCRQueue.Services;
using Xunit;

namespace OCRQueue.Tests;

public class OcrQueueRepositoryTests
{
    private readonly Mock<IPathService> _mockPathService;
    private readonly Mock<ILogger<OcrQueueRepository>> _mockLogger;

    public OcrQueueRepositoryTests()
    {
        _mockPathService = new Mock<IPathService>();
        _mockLogger = new Mock<ILogger<OcrQueueRepository>>();
    }

    private OcrQueueRepository CreateRepository()
    {
        return new OcrQueueRepository(_mockPathService.Object, _mockLogger.Object);
    }

    #region CreateJobAsync Tests

    [Fact]
    public async Task CreateJobAsync_PersistsJobWithQueuedStatus()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange
            int batchId;
            int invoiceId;
            using (var db = CreateOrgContext(dbPath))
            {
                var batch = new Batch
                {
                    Name = "Batch A",
                    BatchNumber = 1,
                    FolderPath = "/path/to/batch",
                    CreatedByUserId = "user-123",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Batches.Add(batch);
                await db.SaveChangesAsync();
                batchId = batch.BatchId;

                var invoice = new Invoice
                {
                    BatchId = batchId,
                    UploadedAtUtc = DateTime.UtcNow,
                    TotalPages = 1
                };
                db.Invoices.Add(invoice);
                await db.SaveChangesAsync();
                invoiceId = invoice.Id;
            }

            var job = new OcrJob(
                JobId: 0,
                InvoiceId: invoiceId,
                BatchId: batchId,
                OrgId: "org-123",
                OrgName: orgName,
                FilePath: "/path/to/invoice.pdf",
                WorkflowKey: "Default",
                QueuedAtUtc: DateTime.UtcNow
            );

            // Act
            var created = await repository.CreateJobAsync(job, orgName);

            // Assert
            Assert.NotNull(created);
            Assert.True(created.Id > 0, "Job ID should be assigned by database");
            Assert.Equal(1, created.InvoiceId);
            Assert.Equal(1, created.BatchId);
            Assert.Equal("org-123", created.OrgId);
            Assert.Equal(orgName, created.OrgName);
            Assert.Equal("/path/to/invoice.pdf", created.FilePath);
            Assert.Equal("Default", created.WorkflowKey);
            Assert.Equal(OcrJobStatus.Queued, created.Status);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region GetPendingJobsAsync Tests

    [Fact]
    public async Task GetPendingJobsAsync_ReturnsQueuedAndProcessingJobs()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange - Create some jobs in different states
            using (var db = CreateOrgContext(dbPath))
            {
                await db.OcrJobs.AddAsync(new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file1.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-5)
                });

                await db.OcrJobs.AddAsync(new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file2.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Processing,
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-3)
                });

                await db.OcrJobs.AddAsync(new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file3.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Completed,
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    CompletedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }

            // Act
            var pending = await repository.GetPendingJobsAsync(orgName);

            // Assert
            Assert.NotEmpty(pending);
            Assert.Equal(2, pending.Count);
            Assert.All(pending, job => 
                Assert.True(
                    job.Status == OcrJobStatus.Queued || job.Status == OcrJobStatus.Processing,
                    "Should only return Queued or Processing jobs"));
            
            // Verify ordering by QueuedAtUtc (oldest first)
            var ordered = pending.OrderBy(j => j.QueuedAtUtc).ToList();
            Assert.Equal(ordered.Select(j => j.Id), pending.Select(j => j.Id));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task GetPendingJobsAsync_ReturnsEmptyListWhenNoPendingJobs()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange - Create only completed/failed jobs
            using (var db = CreateOrgContext(dbPath))
            {
                await db.OcrJobs.AddAsync(new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file1.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Completed,
                    QueuedAtUtc = DateTime.UtcNow.AddHours(-1),
                    CompletedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }

            // Act
            var pending = await repository.GetPendingJobsAsync(orgName);

            // Assert
            Assert.Empty(pending);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region MarkQueuedAsync Tests

    [Fact]
    public async Task MarkQueuedAsync_ResetsJobToQueuedStatus()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange
            int jobId;
            using (var db = CreateOrgContext(dbPath))
            {
                var entity = new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Processing,
                    StartedAtUtc = DateTime.UtcNow,
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-5)
                };
                db.OcrJobs.Add(entity);
                await db.SaveChangesAsync();
                jobId = entity.Id;
            }

            // Act
            await repository.MarkQueuedAsync(jobId, orgName);

            // Assert
            using (var db = CreateOrgContext(dbPath))
            {
                var updated = await db.OcrJobs.FindAsync(jobId);
                Assert.NotNull(updated);
                Assert.Equal(OcrJobStatus.Queued, updated.Status);
                Assert.Null(updated.StartedAtUtc);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region MarkProcessingAsync Tests

    [Fact]
    public async Task MarkProcessingAsync_UpdatesStatusAndStartTime()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange
            int jobId;
            using (var db = CreateOrgContext(dbPath))
            {
                var entity = new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = DateTime.UtcNow
                };
                db.OcrJobs.Add(entity);
                await db.SaveChangesAsync();
                jobId = entity.Id;
            }

            // Act
            var beforeMark = DateTime.UtcNow;
            await repository.MarkProcessingAsync(jobId, orgName);
            var afterMark = DateTime.UtcNow;

            // Assert
            using (var db = CreateOrgContext(dbPath))
            {
                var updated = await db.OcrJobs.FindAsync(jobId);
                Assert.NotNull(updated);
                Assert.Equal(OcrJobStatus.Processing, updated.Status);
                Assert.NotNull(updated.StartedAtUtc);
                Assert.True(updated.StartedAtUtc >= beforeMark && updated.StartedAtUtc <= afterMark);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region MarkCompletedAsync Tests

    [Fact]
    public async Task MarkCompletedAsync_UpdatesStatusAndCompletionTime()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Arrange
            int jobId;
            using (var db = CreateOrgContext(dbPath))
            {
                var entity = new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Processing,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-10)
                };
                db.OcrJobs.Add(entity);
                await db.SaveChangesAsync();
                jobId = entity.Id;
            }

            // Act
            var beforeMark = DateTime.UtcNow;
            await repository.MarkCompletedAsync(jobId, orgName);
            var afterMark = DateTime.UtcNow;

            // Assert
            using (var db = CreateOrgContext(dbPath))
            {
                var updated = await db.OcrJobs.FindAsync(jobId);
                Assert.NotNull(updated);
                Assert.Equal(OcrJobStatus.Completed, updated.Status);
                Assert.NotNull(updated.CompletedAtUtc);
                Assert.True(updated.CompletedAtUtc >= beforeMark && updated.CompletedAtUtc <= afterMark);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_UpdatesStatusAndErrorMessage()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created through migrations so repository calls can migrate safely.
            }

            // Arrange
            int jobId;
            using (var db = CreateOrgContext(dbPath))
            {
                var entity = new OcrJobEntity
                {
                    OrgId = "org1",
                    OrgName = orgName,
                    FilePath = "file.pdf",
                    WorkflowKey = "Default",
                    Status = OcrJobStatus.Processing,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    QueuedAtUtc = DateTime.UtcNow.AddMinutes(-10)
                };
                db.OcrJobs.Add(entity);
                await db.SaveChangesAsync();
                jobId = entity.Id;
            }

            // Act
            var errorMsg = "File not found: /path/to/file.pdf";
            await repository.MarkFailedAsync(jobId, orgName, errorMsg);

            // Assert
            using (var db = CreateOrgContext(dbPath))
            {
                var updated = await db.OcrJobs.FindAsync(jobId);
                Assert.NotNull(updated);
                Assert.Equal(OcrJobStatus.Failed, updated.Status);
                Assert.Equal(errorMsg, updated.ErrorMessage);
                Assert.NotNull(updated.CompletedAtUtc);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task MarkFailedAsync_NoOpIfJobDoesNotExist()
    {
        var repository = CreateRepository();
        var orgName = "TestOrg";
        var dbPath = GetTempDbPath();

        _mockPathService.Setup(ps => ps.GetOrgDbPath(orgName)).Returns(dbPath);

        try
        {
            // Initialize database with FK constraints disabled
            using (var initDb = OpenOrgDbForTest(orgName))
            {
                // Database is created and FK constraints are disabled
            }

            // Arrange - Create a DB with no jobs
            using (var db = CreateOrgContext(dbPath))
            {
                await db.SaveChangesAsync();
            }

            // Act - Should not throw
            await repository.MarkFailedAsync(999, orgName, "Job not found");

            // Assert - Job still doesn't exist, no exception
            using (var db = CreateOrgContext(dbPath))
            {
                var job = await db.OcrJobs.FindAsync(999);
                Assert.Null(job);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    #endregion

    #region Helpers

    private static string GetTempDbPath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        return tempPath;
    }

    private static KOCRDbContext CreateOrgContext(string dbPath)
    {
        var opts = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new KOCRDbContext(opts);
        ctx.Database.Migrate();
        
        return ctx;
    }

    private KOCRDbContext OpenOrgDbForTest(string orgName)
    {
        var dbPath = _mockPathService.Object.GetOrgDbPath(orgName);
        var opts = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new KOCRDbContext(opts);
        ctx.Database.Migrate();
        
        return ctx;
    }

    private static void CleanupDatabase(string dbPath)
    {
        // Clear SQLite connection pool to release file locks
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    #endregion
}
