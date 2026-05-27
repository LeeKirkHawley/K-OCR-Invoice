using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using K_OCR.Configuration;
using Serilog;

// The configuration service reads/writes a JSON settings file.
// In Development: reads a local override file (appsettings.development.user.json)
//   if it exists, otherwise the roaming %AppData%\K-OCR\appsettings.json.
// In Production: reads/writes the deployed appsettings.json in the app base
//   directory (AppDomain.CurrentDomain.BaseDirectory). Saves use a JSON merge
//   so that ASP.NET Core fields (AllowedHosts, Logging, etc.) are preserved.

namespace K_OCR.Services;

public class ConfigurationService : IConfigurationService
{
    private const string DefaultSettingsFileName = "appsettings.json";

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
            Limits = new K_OCR.Configuration.LimitsSection()
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

    public async Task<AppSettings> LoadSettingsAsync(string? path = null)
    {
        var defaultPath = GetDefaultSettingsPath();
        var devOverride = GetDevelopmentOverridePath();

        if (IsDevelopment())
        {
            // prefer the development override file if it exists, otherwise
            // fall back to the roaming file (read‑only baseline).
            path = File.Exists(devOverride) ? devOverride : defaultPath;
        }
        else
        {
            path ??= defaultPath;
        }

        if (!File.Exists(path))
        {
            return GetDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            settings ??= GetDefaultSettings();
            settings.Email ??= new EmailSettings();
            return settings;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings from {Path}; using defaults.", path);
            return GetDefaultSettings();
        }
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

        foreach (var kvp in updates)
        {
            root[kvp.Key] = kvp.Value?.DeepClone();
        }

        await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private string GetDevelopmentOverridePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.development.user.json");
    }

    public int GetGuestMaxBatches()
    {
        try
        {
            var node = ParseSettingsNode();
            if (node?["Limits"]?["Guest"]?["MaxBatches"] is { } v1 && v1.GetValueKind() == System.Text.Json.JsonValueKind.Number) return (int)v1;
            if (node?["GuestMaxBatches"]                 is { } v2 && v2.GetValueKind() == System.Text.Json.JsonValueKind.Number) return (int)v2;
        }
        catch { /* fall through */ }
        return 2;
    }

    public int GetMaxPagesPerInvoice(bool isGuest)
    {
        try
        {
            var node = ParseSettingsNode();
            var key = isGuest ? "Guest" : "User";
            if (node?["Limits"]?[key]?["MaxPagesPerInvoice"] is { } val && val.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                return (int)val;
        }
        catch { /* fall through */ }
        return 20;
    }

    public int GetMaxInvoicesPerBatch(bool isGuest)
    {
        try
        {
            var node = ParseSettingsNode();
            var key = isGuest ? "Guest" : "User";
            if (node?["Limits"]?[key]?["MaxInvoicesPerBatch"] is { } val && val.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                return (int)val;
        }
        catch { /* fall through */ }
        return isGuest ? 20 : 100;
    }

    private System.Text.Json.Nodes.JsonNode? ParseSettingsNode()
    {
        var path = IsDevelopment()
            ? (File.Exists(GetDevelopmentOverridePath()) ? GetDevelopmentOverridePath() : GetDefaultSettingsPath())
            : GetDefaultSettingsPath();

        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return System.Text.Json.Nodes.JsonNode.Parse(json);
    }

    private static bool IsDevelopment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);
    }
}
