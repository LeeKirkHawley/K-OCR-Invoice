using System.IO;
using System.Text.Json;
using K_OCR.Configuration;
using Serilog;

// The configuration service works with a roaming JSON file by default but
// provides a development override to avoid touching the real file during
// local runs.  The override lives in the current directory and is
// automatically used for saves when ASPNETCORE_ENVIRONMENT is
// Development.

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
            MaxConcurrentRequests = 3
        };
    }

    /// <summary>
    /// Path to the *canonical* settings file – always the roaming AppData
    /// location.  We never write to this file when running in development;
    /// instead a separate override file is used so that the roaming file
    /// remains untouched.
    /// </summary>
    public string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "K-OCR");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, DefaultSettingsFileName);
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
        if (IsDevelopment())
            path = GetDevelopmentOverridePath();
        else
            path ??= GetDefaultSettingsPath();

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json);
    }

    private string GetDevelopmentOverridePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.development.user.json");
    }

    private static bool IsDevelopment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);
    }
}
