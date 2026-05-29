using K_OCR.Models;
using K_OCR.Services;
using K_OCR.Data;
using K_OCR.Identity;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Services;

public class SuperAdminServiceTests
{
    [Fact]
    public async Task CleanupExpiredGuestAccountsAsync_MarksOnlyGuestOrganizationsForDeletion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

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

            var guestOrg = new Organization
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Guest1Organization",
                Description = "Guest organization for Guest1",
                IsActive = true,
                IsGuestOrganization = true,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
            };

            var regularOrg = new Organization
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Acme",
                Description = "Regular organization",
                IsActive = true,
                IsGuestOrganization = false,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
            };

            await dbContext.Organizations.AddRangeAsync(guestOrg, regularOrg);
            await dbContext.SaveChangesAsync();

            var guestFolder = Path.Combine(tempRoot, "Guest1Organization");
            var regularFolder = Path.Combine(tempRoot, "Acme");
            Directory.CreateDirectory(guestFolder);
            Directory.CreateDirectory(regularFolder);

            var userStore = new Mock<IUserStore<ApplicationUser>>();
            var userManager = new Mock<UserManager<ApplicationUser>>(
                userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            var roleManager = new Mock<RoleManager<IdentityRole>>(
                Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);

            var service = new SuperAdminService(
                dbContext,
                userManager.Object,
                roleManager.Object,
                Mock.Of<IEmailService>(),
                configuration,
                new PathService(configuration),
                Mock.Of<IOrgConfigService>(),
                null,
                Mock.Of<ILogger<SuperAdminService>>());

            // Phase 1: expired guest org gets marked for deletion; regular org is untouched.
            var markedAccounts = await service.CleanupExpiredGuestAccountsAsync(TimeSpan.FromMinutes(30));

            await using var midContext = new ApplicationDbContext(options);
            Assert.Equal(1, markedAccounts.Count);
            Assert.True(await midContext.Organizations.AnyAsync(o => o.Id == guestOrg.Id));
            Assert.NotNull(await midContext.Organizations
                .Where(o => o.Id == guestOrg.Id)
                .Select(o => o.MarkedForDeletionAtUtc)
                .FirstAsync());
            Assert.True(await midContext.Organizations.AnyAsync(o => o.Id == regularOrg.Id));

            // Backdate the mark so the sweep considers it expired.
            var toExpire = await midContext.Organizations.FirstAsync(o => o.Id == guestOrg.Id);
            toExpire.MarkedForDeletionAtUtc = DateTime.UtcNow.AddHours(-2);
            await midContext.SaveChangesAsync();

            // Phase 2: sweep hard-deletes the expired marked org; regular org survives.
            var deletedCount = await service.CleanupExpiredSoftDeletesAsync(TimeSpan.FromMinutes(30));

            await using var verifyContext = new ApplicationDbContext(options);

            Assert.Equal(1, deletedCount);
            Assert.False(await verifyContext.Organizations.AnyAsync(org => org.Id == guestOrg.Id));
            Assert.True(await verifyContext.Organizations.AnyAsync(org => org.Id == regularOrg.Id));
            Assert.False(Directory.Exists(guestFolder));
            Assert.True(Directory.Exists(regularFolder));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PromoteOrganizationAsync_RenamesStorageAndRetainsInvoicePaths()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

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

                var organization = new Organization
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Guest1Organization",
                    IsActive = true,
                    IsGuestOrganization = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                await dbContext.Organizations.AddAsync(organization);
                await dbContext.SaveChangesAsync();

                var pathService = new PathService(configuration);
                var oldOrgPath = pathService.GetOrgFolderPath(organization.Name);
                Directory.CreateDirectory(oldOrgPath);

                var oldDbPath = pathService.GetOrgDbPath(organization.Name);
                var orgDbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
                    .UseSqlite($"Data Source={oldDbPath}")
                    .Options;

                await using (var orgDb = new KOCRDbContext(orgDbOptions))
                {
                    await orgDb.Database.MigrateAsync();
                    var batchFolder = Path.Combine(oldOrgPath, "BatchA");
                    var invoicePath = Path.Combine(batchFolder, "Invoices", "a.pdf");
                    orgDb.Batches.Add(new Batch
                    {
                        Name = "BatchA",
                        BatchNumber = 1,
                        FolderPath = batchFolder,
                        CreatedByUserId = "u1"
                    });
                    await orgDb.SaveChangesAsync();

                    var batchId = await orgDb.Batches.Select(b => b.BatchId).FirstAsync();
                    orgDb.Invoices.Add(new Invoice
                    {
                        BatchId = batchId,
                        FilePath = invoicePath,
                        TotalPages = 2,
                        IsFullyProcessed = true,
                        ProcessedAtUtc = DateTime.UtcNow
                    });
                    orgDb.OcrJobs.Add(new OcrJobEntity
                    {
                        BatchId = batchId,
                        OrgId = organization.Id,
                        OrgName = organization.Name,
                        FilePath = invoicePath
                    });
                    await orgDb.SaveChangesAsync();
                }

                SqliteConnection.ClearAllPools();

                var userStore = new Mock<IUserStore<ApplicationUser>>();
                var userManager = new Mock<UserManager<ApplicationUser>>(
                    userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
                var roleManager = new Mock<RoleManager<IdentityRole>>(
                    Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);

                var service = new SuperAdminService(
                    dbContext,
                    userManager.Object,
                    roleManager.Object,
                    Mock.Of<IEmailService>(),
                    configuration,
                    pathService,
                    Mock.Of<IOrgConfigService>(),
                    null,
                    Mock.Of<ILogger<SuperAdminService>>());

                var result = await service.PromoteOrganizationAsync(organization.Id, "AcmePromoted");

                Assert.Equal("AcmePromoted", result.OrganizationName);
                Assert.False(result.StripeProvisioned);

                await using var verifyIdentity = new ApplicationDbContext(options);
                var promoted = await verifyIdentity.Organizations.FirstAsync(o => o.Id == organization.Id);
                Assert.Equal("AcmePromoted", promoted.Name);
                Assert.False(promoted.IsGuestOrganization);
                Assert.False(promoted.IsBetaTestOrganization);

                var newOrgPath = pathService.GetOrgFolderPath("AcmePromoted");
                Assert.False(Directory.Exists(oldOrgPath));
                Assert.True(Directory.Exists(newOrgPath));

                var newDbPath = pathService.GetOrgDbPath("AcmePromoted");
                var verifyOrgDbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
                    .UseSqlite($"Data Source={newDbPath}")
                    .Options;
                await using var verifyOrgDb = new KOCRDbContext(verifyOrgDbOptions);

                var batch = await verifyOrgDb.Batches.SingleAsync();
                var invoice = await verifyOrgDb.Invoices.SingleAsync();
                var job = await verifyOrgDb.OcrJobs.SingleAsync();

                Assert.StartsWith(newOrgPath, batch.FolderPath, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(newOrgPath, invoice.FilePath!, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(newOrgPath, job.FilePath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("AcmePromoted", job.OrgName);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
        }
}
