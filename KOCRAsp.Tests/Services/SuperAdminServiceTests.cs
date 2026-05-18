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
}

