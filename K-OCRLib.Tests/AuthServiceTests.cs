using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Models.Api.Auth;
using K_OCR.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_ReturnsTenantAndRoles()
    {
        var (db, provider, connection) = await CreateHarnessAsync();
        try
        {
            var org = new Organization { Id = Guid.NewGuid().ToString(), Name = "Acme" };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();

            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            await provider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole("OrganizationAdmin"));

            var user = new ApplicationUser
            {
                UserName = "user@acme.test",
                Email = "user@acme.test",
                FullName = "User",
                OrganizationId = org.Id
            };
            var create = await userManager.CreateAsync(user, "Password123!");
            Assert.True(create.Succeeded);
            await userManager.AddToRoleAsync(user, "OrganizationAdmin");

            var service = new AuthService(userManager, Mock.Of<ILogger<AuthService>>());
            var result = await service.LoginAsync(new LoginRequest { Email = user.Email!, Password = "Password123!" });

            Assert.Equal(org.Id, result.TenantId);
            Assert.Equal("Acme", result.TenantName);
            Assert.Contains("OrganizationAdmin", result.Roles);
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_ConfirmsEmail()
    {
        var (db, provider, connection) = await CreateHarnessAsync();
        try
        {
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "user@acme.test",
                Email = "user@acme.test",
                FullName = "User"
            };
            await userManager.CreateAsync(user, "Password123!");
            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            var service = new AuthService(userManager, Mock.Of<ILogger<AuthService>>());
            await service.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = user.Email!,
                Token = token,
                NewPassword = "NewPassword123!"
            });

            var reloaded = await userManager.FindByEmailAsync(user.Email!);
            Assert.True(reloaded!.EmailConfirmed);
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
        }
    }

    [Fact]
    public async Task GeneratePasswordResetLinkAsync_ReturnsEncodedLink()
    {
        var (db, provider, connection) = await CreateHarnessAsync();
        try
        {
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "user@acme.test",
                Email = "user@acme.test",
                FullName = "User"
            };
            await userManager.CreateAsync(user, "Password123!");

            var service = new AuthService(userManager, Mock.Of<ILogger<AuthService>>());
            var link = await service.GeneratePasswordResetLinkAsync(user.Email!, "https://example.com");

            Assert.Contains("/auth/setpassword?", link);
            Assert.Contains(Uri.EscapeDataString(user.Id), link);
        }
        finally
        {
            await db.DisposeAsync();
            connection.Dispose();
        }
    }

    private static async Task<(ApplicationDbContext db, ServiceProvider provider, SqliteConnection connection)> CreateHarnessAsync()
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
}
