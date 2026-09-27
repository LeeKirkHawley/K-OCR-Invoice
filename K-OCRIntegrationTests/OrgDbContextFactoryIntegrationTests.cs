using K_OCR.Services;
using K_OCRLib.Configuration;
using K_OCRLib.Data;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace K_OCRIntegrationTests;

/// <summary>
/// End-to-end coverage for OrgDbContextFactory + KOCRDbContext against a real, migrated
/// SQLite file (no in-memory provider, no mocked DbContext). Only the tenant/path inputs
/// are mocked, since they are simple value providers rather than the database itself.
/// </summary>
public class OrgDbContextFactoryIntegrationTests
{
    [Fact]
    public void CreateDbContext_MigratesAndPersistsRealSqliteDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"org-{Guid.NewGuid():N}.db");
        var factory = CreateFactory(orgName: "Integration Test Org", dbPath: dbPath);

        try
        {
            using (var context = factory.CreateDbContext())
            {
                Assert.True(File.Exists(dbPath));
                Assert.True(context.Database.CanConnect());

                context.Batches.Add(new Batch
                {
                    Name = "Batch One",
                    BatchNumber = 1,
                    FolderPath = "Batch One",
                    CreatedByUserId = "user-1"
                });
                context.SaveChanges();
            }

            // Re-open against the same on-disk file to prove the write really persisted.
            using var reopened = factory.CreateDbContext();
            var batch = reopened.Batches.Single();
            Assert.Equal("Batch One", batch.Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void CreateDbContext_ThrowsForSuperAdmin()
    {
        var factory = CreateFactory(isSuperAdmin: true, orgName: "Integration Test Org");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());
        Assert.Contains("super-admin", ex.Message);
    }

    private static OrgDbContextFactory CreateFactory(bool isSuperAdmin = false, string? orgName = null, string? dbPath = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsSuperAdmin).Returns(isSuperAdmin);
        tenant.SetupGet(t => t.OrganizationName).Returns(orgName);

        var pathService = new Mock<IPathService>();
        pathService.Setup(p => p.GetOrgDbPath(It.IsAny<string>()))
            .Returns(dbPath ?? Path.Combine(Path.GetTempPath(), "unused.db"));

        return new OrgDbContextFactory(tenant.Object, pathService.Object, new DatabaseSettings());
    }
}
