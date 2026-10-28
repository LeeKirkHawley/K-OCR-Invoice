using System.Text.Json.Nodes;
using K_OCRLib.Configuration;
using K_OCRLib.Services;
using Microsoft.Extensions.Configuration;

namespace K_OCRLib.Tests;

public class ConfigurationServiceTests
{
    private static ConfigurationService CreateService(Dictionary<string, string?>? values = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build());

    [Fact]
    public void GetDefaultSettings_ReturnsExpectedValues()
    {
        var settings = CreateService().GetDefaultSettings();

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
    public async Task LoadSettingsAsync_ReturnsDefaultsWhenConfigurationEmpty()
    {
        var settings = await CreateService().LoadSettingsAsync();

        Assert.Equal("Azure", settings.OCRProvider);
        Assert.Equal(2, settings.GuestMaxBatches);
        Assert.NotNull(settings.Email);
        Assert.Equal("Elk Mountain Invoice", settings.Email.FromName);
    }

    [Fact]
    public async Task LoadSettingsAsync_BindsValuesFromConfiguration()
    {
        var service = CreateService(new()
        {
            ["Email:SmtpHost"] = "smtp.test",
            ["Email:Password"] = "from-secrets",
            ["OCRProvider"] = "Tesseract"
        });

        var settings = await service.LoadSettingsAsync();

        Assert.Equal("smtp.test", settings.Email.SmtpHost);
        Assert.Equal("from-secrets", settings.Email.Password);
        Assert.Equal("Tesseract", settings.OCRProvider);
    }

    [Fact]
    public async Task SaveSettingsAsync_MergesExistingJson()
    {
        var service = CreateService();
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, """
        {
          "AllowedHosts": "*",
          "Logging": { "LogLevel": { "Default": "Information" } }
        }
        """);

        try
        {
            var settings = new AppSettings
            {
                OCRProvider = "Tesseract",
                GoogleAnalyticsMeasurementId = "G-TEST123",
                Limits = new LimitsSection
                {
                    Guest = new GuestLimits { MaxBatches = 6 },
                    User = new UserLimits { MaxInvoicesPerBatch = 9 }
                }
            };

            await service.SaveSettingsAsync(settings, path);

            var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.Equal("*", root["AllowedHosts"]!.GetValue<string>());
            Assert.Equal("Tesseract", root["OCRProvider"]!.GetValue<string>());
            Assert.Equal("G-TEST123", root["GoogleAnalyticsMeasurementId"]!.GetValue<string>());
            Assert.Equal(6, root["Limits"]!["Guest"]!["MaxBatches"]!.GetValue<int>());
            Assert.Equal(9, root["Limits"]!["User"]!["MaxInvoicesPerBatch"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveSettingsAsync_DoesNotWriteSecretsToFile()
    {
        var service = CreateService(new() { ["Email:Password"] = "from-secrets" });
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid()}.json");

        try
        {
            var settings = await service.LoadSettingsAsync();
            settings.Email.SmtpHost = "smtp.test";
            settings.AzureCognitiveServicesKey = "azure-key";

            await service.SaveSettingsAsync(settings, path);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("smtp.test", json);
            Assert.DoesNotContain("from-secrets", json);
            Assert.DoesNotContain("azure-key", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveSettingsAsync_KeepsSecretAlreadyInFile()
    {
        var service = CreateService();
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, """{ "Email": { "Password": "deployed-secret" } }""");

        try
        {
            var settings = new AppSettings { Email = new EmailSettings { SmtpHost = "smtp.test", Password = "typed-in-ui" } };

            await service.SaveSettingsAsync(settings, path);

            var email = JsonNode.Parse(await File.ReadAllTextAsync(path))!["Email"]!;
            Assert.Equal("smtp.test", (string?)email["SmtpHost"]);
            Assert.Equal("deployed-secret", (string?)email["Password"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetGuestAndInvoiceLimits_ReadFromConfiguration()
    {
        var service = CreateService(new()
        {
            ["Limits:Guest:MaxBatches"] = "4",
            ["Limits:Guest:MaxInvoicesPerBatch"] = "5",
            ["Limits:Guest:MaxPagesPerInvoice"] = "6",
            ["Limits:User:MaxInvoicesPerBatch"] = "7",
            ["Limits:User:MaxPagesPerInvoice"] = "8"
        });

        Assert.Equal(4, service.GetGuestMaxBatches());
        Assert.Equal(6, service.GetMaxPagesPerInvoice(true));
        Assert.Equal(8, service.GetMaxPagesPerInvoice(false));
        Assert.Equal(5, service.GetMaxInvoicesPerBatch(true));
        Assert.Equal(7, service.GetMaxInvoicesPerBatch(false));
    }

    [Fact]
    public void Limits_FallBackToDefaultsWhenNotConfigured()
    {
        var service = CreateService();

        Assert.Equal(2, service.GetGuestMaxBatches());
        Assert.Equal(20, service.GetMaxPagesPerInvoice(true));
        Assert.Equal(20, service.GetMaxInvoicesPerBatch(true));
        Assert.Equal(100, service.GetMaxInvoicesPerBatch(false));
        Assert.Equal(500, service.GetBetaMaxOcrPages());
    }
}
