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

public class SuperAdminServiceEmailTests
{
    [Fact]
    public async Task MarkOrganizationForDeletionAsync_SendsEmailToOrgAdmins()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var org = new Organization
        {
            Id = Guid.NewGuid().ToString(),
            Name = "TestOrg",
            Description = "Test organization",
            IsActive = false,
            IsGuestOrganization = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin@test.com",
            Email = "admin@test.com",
            FullName = "Test Admin",
            IsOrganizationAdmin = true
        };

        var regularUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "user@test.com",
            Email = "user@test.com",
            FullName = "Regular User",
            IsOrganizationAdmin = false
        };

        org.Users.Add(admin);
        org.Users.Add(regularUser);

        await dbContext.Organizations.AddAsync(org);
        await dbContext.Users.AddRangeAsync(admin, regularUser);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = Path.GetTempPath()
            })
            .Build();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var roleManager = new Mock<RoleManager<IdentityRole>>(
            Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);
        
        var emailServiceMock = new Mock<IEmailService>();

        var service = new SuperAdminService(
            dbContext,
            userManager.Object,
            roleManager.Object,
            emailServiceMock.Object,
            configuration,
            new PathService(configuration),
            Mock.Of<IOrgConfigService>(),
            null,
            Mock.Of<ILogger<SuperAdminService>>());

        // Act
        await service.MarkOrganizationForDeletionAsync(org.Id);

        // Assert: Email should be sent only to organization admin, not regular user
        emailServiceMock.Verify(
            x => x.SendOrgDeletionNotificationAsync(
                "admin@test.com",
                "Test Admin",
                "TestOrg"),
            Times.Once,
            "Email should be sent to organization admin");

        // Email should not be sent to regular user
        emailServiceMock.Verify(
            x => x.SendOrgDeletionNotificationAsync(
                "user@test.com",
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never,
            "Email should not be sent to non-admin users");

        // Verify organization is marked for deletion
        var markedOrg = await dbContext.Organizations.FirstAsync(o => o.Id == org.Id);
        Assert.NotNull(markedOrg.MarkedForDeletionAtUtc);
    }

    [Fact]
    public async Task MarkOrganizationForDeletionAsync_HandlesEmailSendingErrors()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var org = new Organization
        {
            Id = Guid.NewGuid().ToString(),
            Name = "TestOrg",
            IsActive = false,
            IsGuestOrganization = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin@test.com",
            Email = "admin@test.com",
            FullName = "Test Admin",
            IsOrganizationAdmin = true
        };

        org.Users.Add(admin);
        await dbContext.Organizations.AddAsync(org);
        await dbContext.Users.AddAsync(admin);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = Path.GetTempPath()
            })
            .Build();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var roleManager = new Mock<RoleManager<IdentityRole>>(
            Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);
        
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(x => x.SendOrgDeletionNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP error"));

        var service = new SuperAdminService(
            dbContext,
            userManager.Object,
            roleManager.Object,
            emailServiceMock.Object,
            configuration,
            new PathService(configuration),
            Mock.Of<IOrgConfigService>(),
            null,
            Mock.Of<ILogger<SuperAdminService>>());

        // Act & Assert: Should not throw even if email fails
        await service.MarkOrganizationForDeletionAsync(org.Id);

        // Organization should still be marked for deletion
        var markedOrg = await dbContext.Organizations.FirstAsync(o => o.Id == org.Id);
        Assert.NotNull(markedOrg.MarkedForDeletionAtUtc);
    }

    [Fact]
    public async Task MarkOrganizationForDeletionAsync_SkipsAdminWithoutEmail()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var org = new Organization
        {
            Id = Guid.NewGuid().ToString(),
            Name = "TestOrg",
            IsActive = false,
            IsGuestOrganization = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var adminWithEmail = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin1@test.com",
            Email = "admin1@test.com",
            FullName = "Admin With Email",
            IsOrganizationAdmin = true
        };

        var adminWithoutEmail = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin2",
            Email = null,
            FullName = "Admin Without Email",
            IsOrganizationAdmin = true
        };

        org.Users.Add(adminWithEmail);
        org.Users.Add(adminWithoutEmail);

        await dbContext.Organizations.AddAsync(org);
        await dbContext.Users.AddRangeAsync(adminWithEmail, adminWithoutEmail);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = Path.GetTempPath()
            })
            .Build();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var roleManager = new Mock<RoleManager<IdentityRole>>(
            Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);
        
        var emailServiceMock = new Mock<IEmailService>();

        var service = new SuperAdminService(
            dbContext,
            userManager.Object,
            roleManager.Object,
            emailServiceMock.Object,
            configuration,
            new PathService(configuration),
            Mock.Of<IOrgConfigService>(),
            null,
            Mock.Of<ILogger<SuperAdminService>>());

        // Act
        await service.MarkOrganizationForDeletionAsync(org.Id);

        // Assert: Email should only be sent to admin with email
        emailServiceMock.Verify(
            x => x.SendOrgDeletionNotificationAsync(
                "admin1@test.com",
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        // Email should not be attempted for admin without email
        emailServiceMock.Verify(
            x => x.SendOrgDeletionNotificationAsync(
                null!,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }
}


