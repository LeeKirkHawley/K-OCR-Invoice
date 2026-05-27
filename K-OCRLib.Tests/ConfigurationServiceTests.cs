using System.Text.Json.Nodes;
using K_OCR.Configuration;
using K_OCR.Services;

namespace K_OCRLib.Tests;

public class ConfigurationServiceTests
{
    [Fact]
    public void GetDefaultSettings_ReturnsExpectedValues()
    {
        var service = new ConfigurationService();
        var settings = service.GetDefaultSettings();

        Assert.Equal("https://parsedocimage.cognitiveservices.azure.com/", settings.AzureCognitiveServicesEndpoint);
        Assert.Equal("Azure", settings.OCRProvider);
        Assert.Equal(3, settings.MaxConcurrentRequests);
        Assert.Equal(7, settings.GuestAccountRetentionDays);
        Assert.Equal(2, settings.GuestMaxBatches);
        Assert.Null(settings.GoogleAnalyticsMeasurementId);
        Assert.Null(settings.SearchConsoleVerificationToken);
        Assert.NotNull(settings.Limits);
        Assert.NotNull(settings.Email);
    }

    [Fact]
    public async Task LoadSettingsAsync_ReturnsDefaultsWhenFileMissing()
    {
        await WithProductionEnvironment(async () =>
        {
            var service = new ConfigurationService();
            var settings = await service.LoadSettingsAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));

            Assert.Equal("Azure", settings.OCRProvider);
            Assert.Equal(2, settings.GuestMaxBatches);
        });
    }

    [Fact]
    public async Task SaveSettingsAsync_MergesExistingJson()
    {
        await WithProductionEnvironment(async () =>
        {
            var service = new ConfigurationService();
            var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid()}.json");
            await File.WriteAllTextAsync(path, """
            {
              "AllowedHosts": "*",
              "Logging": {
                "LogLevel": {
                  "Default": "Information"
                }
              }
            }
            """);

            try
            {
                var settings = new AppSettings
                {
                    OCRProvider = "Tesseract",
                    GuestMaxBatches = 5,
                    GoogleAnalyticsMeasurementId = "G-TEST123",
                    SearchConsoleVerificationToken = "verify-token",
                    Limits = new LimitsSection
                    {
                        Guest = new GuestLimits { MaxBatches = 6, MaxInvoicesPerBatch = 7, MaxPagesPerInvoice = 8 },
                        User = new UserLimits { MaxInvoicesPerBatch = 9, MaxPagesPerInvoice = 10 }
                    }
                };

                await service.SaveSettingsAsync(settings, path);

                var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
                Assert.Equal("*", root["AllowedHosts"]!.GetValue<string>());
                Assert.Equal("Tesseract", root["OCRProvider"]!.GetValue<string>());
                Assert.Equal("G-TEST123", root["GoogleAnalyticsMeasurementId"]!.GetValue<string>());
                Assert.Equal("verify-token", root["SearchConsoleVerificationToken"]!.GetValue<string>());
                Assert.Equal(6, root["Limits"]!["Guest"]!["MaxBatches"]!.GetValue<int>());
                Assert.Equal(9, root["Limits"]!["User"]!["MaxInvoicesPerBatch"]!.GetValue<int>());
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        });
    }

    [Fact]
    public async Task LoadSettingsAsync_ReturnsEmailDefaultsWhenEmailSectionMissing()
    {
        await WithProductionEnvironment(async () =>
        {
            var service = new ConfigurationService();
            var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid()}.json");
            await File.WriteAllTextAsync(path, """
            {
              "OCRProvider": "Azure"
            }
            """);

            try
            {
                var settings = await service.LoadSettingsAsync(path);

                Assert.NotNull(settings.Email);
                Assert.Equal("K-OCR", settings.Email.FromName);
                Assert.Null(settings.GoogleAnalyticsMeasurementId);
                Assert.Null(settings.SearchConsoleVerificationToken);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        });
    }

    [Fact]
    public void GetGuestAndInvoiceLimits_ReadDevelopmentOverrideFile()
    {
        RunWithTemporaryCurrentDirectory(dir =>
        {
            var overridePath = Path.Combine(dir, "appsettings.development.user.json");
            File.WriteAllText(overridePath, """
            {
              "Limits": {
                "Guest": {
                  "MaxBatches": 4,
                  "MaxInvoicesPerBatch": 5,
                  "MaxPagesPerInvoice": 6
                },
                "User": {
                  "MaxInvoicesPerBatch": 7,
                  "MaxPagesPerInvoice": 8
                }
              }
            }
            """);

            var service = new ConfigurationService();

            Assert.Equal(4, service.GetGuestMaxBatches());
            Assert.Equal(6, service.GetMaxPagesPerInvoice(true));
            Assert.Equal(8, service.GetMaxPagesPerInvoice(false));
            Assert.Equal(5, service.GetMaxInvoicesPerBatch(true));
            Assert.Equal(7, service.GetMaxInvoicesPerBatch(false));
        });
    }

    private static async Task WithProductionEnvironment(Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previous);
        }
    }

    private static void RunWithTemporaryCurrentDirectory(Action<string> action)
    {
        var previous = Environment.CurrentDirectory;
        var previousEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var dir = Path.Combine(Path.GetTempPath(), $"kocr-config-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.CurrentDirectory = dir;

        try
        {
            action(dir);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnv);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
