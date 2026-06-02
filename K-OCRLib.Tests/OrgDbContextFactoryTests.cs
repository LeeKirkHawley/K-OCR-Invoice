using K_OCR.Services;
using K_OCRLib.Configuration;
using K_OCRLib.Data;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace K_OCRLib.Tests;

public class OrgDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_ThrowsForSuperAdmin()
    {
        var factory = CreateFactory(isSuperAdmin: true, orgName: "TestOrg");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());
        Assert.Contains("super-admin", ex.Message);
    }

    [Fact]
    public void CreateDbContext_ThrowsWhenOrgNameMissing()
    {
        var factory = CreateFactory(isSuperAdmin: false, orgName: null);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());
        Assert.Contains("authenticated org user", ex.Message);
    }

    [Fact]
    public void CreateDbContext_MigratesSqliteDatabaseForOrg()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"org-{Guid.NewGuid()}.db");
        var factory = CreateFactory(isSuperAdmin: false, orgName: "Test Org", dbPath: dbPath);

        try
        {
            using var context = factory.CreateDbContext();

            Assert.True(File.Exists(dbPath));
            Assert.Contains(dbPath, context.Database.GetDbConnection().ConnectionString);
            Assert.True(context.Database.CanConnect());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static OrgDbContextFactory CreateFactory(bool isSuperAdmin, string? orgName, string? dbPath = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsSuperAdmin).Returns(isSuperAdmin);
        tenant.SetupGet(t => t.OrganizationName).Returns(orgName);

        var pathService = new Mock<IPathService>();
        pathService.Setup(p => p.GetOrgDbPath(It.IsAny<string>())).Returns(dbPath ?? Path.Combine(Path.GetTempPath(), "unused.db"));

        return new OrgDbContextFactory(
            tenant.Object,
            pathService.Object,
            new DatabaseSettings
            {
                EnableDetailedErrors = true,
                EnableSensitiveDataLogging = true
            });
    }
}
