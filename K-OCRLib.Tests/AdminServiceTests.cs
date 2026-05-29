using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Models.Api.OrganizationAdmin;
using K_OCR.Services;
using K_OCR.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class AdminServiceTests
{
    [Fact]
    public async Task SuperAdminDataService_GetAllBatchesAcrossOrgsAsync_ReturnsCrossOrgBatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"superdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var appOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "app.db")}")
                .Options;
            await using var appDb = new ApplicationDbContext(appOptions);
            await appDb.Database.EnsureCreatedAsync();

            var org1 = new K_OCR.Identity.Organization { Id = Guid.NewGuid().ToString(), Name = "Org One" };
            var org2 = new K_OCR.Identity.Organization { Id = Guid.NewGuid().ToString(), Name = "Org Two" };
            appDb.Organizations.AddRange(org1, org2);
            await appDb.SaveChangesAsync();

            await SeedOrgDbAsync(root, org1.Name, "B1", 1, 1);
            await SeedOrgDbAsync(root, org2.Name, "B2", 2, 2);

            var pathService = new PathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = root
            }).Build());

            var service = new SuperAdminDataService(appDb, pathService, Mock.Of<ILogger<SuperAdminDataService>>());
            var batches = await service.GetAllBatchesAcrossOrgsAsync();

            Assert.Equal(2, batches.Length);
            Assert.Contains(batches, b => b.OrganizationName == org1.Name && b.Name == "B1");
            Assert.Contains(batches, b => b.OrganizationName == org2.Name && b.Name == "B2");
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task OrganizationAdminService_GetOrgUsersAsync_ReturnsUsersAndRoles()
    {
        var (db, provider, connection) = await CreateIdentityHarnessAsync();
        try
        {
            var org = new K_OCR.Identity.Organization { Id = Guid.NewGuid().ToString(), Name = "Org One" };
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "user@test.com",
                Email = "user@test.com",
                FullName = "User One",
                OrganizationId = org.Id
            };
            db.Organizations.Add(org);
            db.Users.Add(user);
            db.UserOrganizationMemberships.Add(new UserOrganizationMembership
            {
                UserId = user.Id,
                OrganizationId = org.Id,
                Role = RoleNames.OrganizationAdmin
            });
            await db.SaveChangesAsync();

            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var email = new Mock<IEmailService>();
            var service = new OrganizationAdminService(db, userManager, email.Object, Mock.Of<ILogger<OrganizationAdminService>>());

            var users = await service.GetOrgUsersAsync(org.Id);

            Assert.Single(users);
            Assert.Equal("User One", users[0].FullName);
            Assert.Contains(RoleNames.OrganizationAdmin, users[0].Roles);
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task OrganizationAdminService_InviteUserAsync_RejectsInvalidRole()
    {
        var (db, provider, connection) = await CreateIdentityHarnessAsync();
        try
        {
            var org = new K_OCR.Identity.Organization { Id = Guid.NewGuid().ToString(), Name = "Org One", IsActive = true };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();

            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var service = new OrganizationAdminService(db, userManager, Mock.Of<IEmailService>(), Mock.Of<ILogger<OrganizationAdminService>>());

            await Assert.ThrowsAsync<ArgumentException>(() => service.InviteUserAsync(
                org.Id,
                new InviteUserRequest { Email = "user@test.com", Name = "User", Role = "InvalidRole" },
                "https://example.com"));
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task SuperAdminService_CleanupExpiredGuestAccountsAsync_MarksGuestOrganizations()
    {
        var (db, provider, connection) = await CreateIdentityHarnessAsync();
        var root = Path.Combine(Path.GetTempPath(), $"superadmin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var guest = new K_OCR.Identity.Organization
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Guest One",
                IsGuestOrganization = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
            };
            db.Organizations.Add(guest);
            await db.SaveChangesAsync();

            var path = new PathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = root
            }).Build());

            var service = new SuperAdminService(
                db,
                provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<RoleManager<IdentityRole>>(),
                Mock.Of<IEmailService>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowDuplicateEmails"] = "false" }).Build(),
                path,
                Mock.Of<IOrgConfigService>(),
                null,
                Mock.Of<ILogger<SuperAdminService>>());

            var marked = await service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromDays(7));

            Assert.Single(marked);
            Assert.NotNull(await db.Organizations.Where(o => o.Id == guest.Id).Select(o => o.MarkedForDeletionAtUtc).FirstAsync());
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
            CleanupDirectory(root);
        }
    }

    private static async Task SeedOrgDbAsync(string root, string orgName, string batchName, int batchNumber, int invoiceCount)
    {
        var pathService = new PathService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kocr:BaseDirectory"] = root
        }).Build());

        var dbPath = pathService.GetOrgDbPath(orgName);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var options = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var db = new KOCRDbContext(options);
        await db.Database.MigrateAsync();
        var batch = new K_OCR.Models.Batch
        {
            Name = batchName,
            BatchNumber = batchNumber,
            FolderPath = Path.Combine(pathService.GetOrgFolderPath(orgName), batchName),
            CreatedByUserId = "user",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Batches.Add(batch);
        await db.SaveChangesAsync();

        for (var i = 0; i < invoiceCount; i++)
        {
            db.Invoices.Add(new K_OCR.Models.Invoice
            {
                BatchId = batch.BatchId,
                FilePath = $"{batchName}-{i}.pdf",
                UploadedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<(ApplicationDbContext db, ServiceProvider provider, SqliteConnection connection)> CreateIdentityHarnessAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (db, provider, connection);
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
