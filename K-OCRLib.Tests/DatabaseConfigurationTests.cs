using K_OCR.Data;
using K_OCR.Database;
using K_OCR.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace K_OCRLib.Tests;

public class DatabaseConfigurationTests
{
    [Fact]
    public void AddKOCRDatabase_WithConfiguration_RegistersSqliteContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Data Source=test-config.db",
                ["Database:EnableSensitiveDataLogging"] = "true",
                ["Database:EnableDetailedErrors"] = "true"
            })
            .Build();

        services.AddKOCRDatabase(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<KOCRDbContext>();
        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

        Assert.NotNull(dbService);
        Assert.Contains("test-config.db", context.Database.GetDbConnection().ConnectionString);
        Assert.Contains("Cache=Shared", context.Database.GetDbConnection().ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddKOCRDatabase_WithConnectionString_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKOCRDatabase("Data Source=test-legacy.db");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<KOCRDbContext>();
        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

        Assert.NotNull(dbService);
        Assert.Contains("test-legacy.db", context.Database.GetDbConnection().ConnectionString);
    }
}
