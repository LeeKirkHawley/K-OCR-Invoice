using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using K_OCRLib.Configuration;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;

// Settings are read from IConfiguration, so appsettings files, user-secrets (Development)
// and environment variables (e.g. Email__Password) are merged in the standard ASP.NET order.
// The Settings / Email Config pages save edits to a JSON file (Development: the local
// appsettings.development.user.json override; Production: the deployed appsettings.json)
// using a JSON merge so ASP.NET Core fields (AllowedHosts, Logging, etc.) are preserved.
// Secrets are never written into that file.

namespace K_OCRLib.Services;

public class ConfigurationService : IConfigurationService
{
    private const string DefaultSettingsFileName = "appsettings.json";

    private static readonly string[][] SecretPaths =
    [
        ["AzureCognitiveServicesKey"],
        ["Email", "Password"],
        ["Database", "ConnectionString"]
    ];

    private readonly IConfiguration _configuration;

    public ConfigurationService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public AppSettings GetDefaultSettings()
    {
        return new AppSettings
        {
            AzureCognitiveServicesKey = null,
            AzureCognitiveServicesEndpoint = "https://parsedocimage.cognitiveservices.azure.com/",
            OCRProvider = "Azure",
            MaxConcurrentRequests = 3,
            GuestAccountRetentionDays = 7,
            GuestMaxBatches = 2,
            GoogleAnalyticsMeasurementId = null,
            SearchConsoleVerificationToken = null,
            Limits = new LimitsSection()
        };
    }

    /// <summary>
    /// Path to the settings file used at runtime.
    /// Development: roaming %AppData%\K-OCR\appsettings.json.
    /// Production: appsettings.json in the app's own base directory.
    /// </summary>
    public string GetDefaultSettingsPath()
    {
        if (IsDevelopment())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "K-OCR");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, DefaultSettingsFileName);
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultSettingsFileName);
    }

    public Task<AppSettings> LoadSettingsAsync()
    {
        var settings = GetDefaultSettings();
        _configuration.Bind(settings);
        settings.Email ??= new EmailSettings();
        return Task.FromResult(settings);
    }

    public async Task SaveSettingsAsync(AppSettings settings, string? path = null)
    {
        path ??= IsDevelopment() ? GetDevelopmentOverridePath() : GetDefaultSettingsPath();

        // Always use a JSON merge so that keys not owned by AppSettings
        // (e.g. Stripe:*, OcrQueue:*, AllowedHosts) are preserved in the file.
        JsonObject root;
        if (File.Exists(path))
        {
            var existingJson = await File.ReadAllTextAsync(path);
            root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var updates = JsonNode.Parse(settingsJson)!.AsObject();

        KeepSecretsOutOfFile(updates, root);

        foreach (var kvp in updates)
        {
            root[kvp.Key] = kvp.Value?.DeepClone();
        }

        await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Pick up the change immediately instead of waiting for the file watcher.
        (_configuration as IConfigurationRoot)?.Reload();
    }

    // Values from user-secrets / environment variables are bound into the settings object, so
    // they must not be written back to the file. A value already in the file is left as it was.
    private static void KeepSecretsOutOfFile(JsonObject updates, JsonObject existing)
    {
        foreach (var secretPath in SecretPaths)
        {
            var key = secretPath[^1];
            var target = ParentOf(updates, secretPath);
            if (target is null) continue;

            var existingValue = ParentOf(existing, secretPath)?[key];
            target.Remove(key);
            if (existingValue is not null)
                target[key] = existingValue.DeepClone();
        }
    }

    private static JsonObject? ParentOf(JsonObject node, string[] path)
    {
        var current = node;
        foreach (var segment in path[..^1])
        {
            if (current[segment] is not JsonObject next) return null;
            current = next;
        }
        return current;
    }

    private string GetDevelopmentOverridePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.development.user.json");
    }

    public int GetGuestMaxBatches() =>
        _configuration.GetValue<int?>("Limits:Guest:MaxBatches")
        ?? _configuration.GetValue<int?>("GuestMaxBatches")
        ?? 2;

    public int GetMaxPagesPerInvoice(bool isGuest) =>
        _configuration.GetValue<int?>($"Limits:{(isGuest ? "Guest" : "User")}:MaxPagesPerInvoice") ?? 20;

    public int GetMaxInvoicesPerBatch(bool isGuest) =>
        _configuration.GetValue<int?>($"Limits:{(isGuest ? "Guest" : "User")}:MaxInvoicesPerBatch")
        ?? (isGuest ? 20 : 100);

    public int GetBetaMaxOcrPages() =>
        _configuration.GetValue<int?>("Limits:Beta:MaxOcrPages") ?? 500;

    private static bool IsDevelopment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);
    }
}
