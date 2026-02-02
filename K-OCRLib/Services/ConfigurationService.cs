using System.Text.Json;
using K_OCR.Configuration;

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
            DefaultStartDirectory = null,
            MaxConcurrentRequests = 3
        };
    }
    
    public string GetDefaultSettingsPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultSettingsFileName);
    }
    
    public async Task<AppSettings> LoadSettingsAsync(string? path = null)
    {
        path ??= GetDefaultSettingsPath();
        
        if (!File.Exists(path))
        {
            // Return default settings if file doesn't exist
            return GetDefaultSettings();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return settings ?? GetDefaultSettings();
        }
        catch
        {
            // Return default settings if deserialization fails
            return GetDefaultSettings();
        }
    }
    
    public async Task SaveSettingsAsync(AppSettings settings, string? path = null)
    {
        path ??= GetDefaultSettingsPath();
        
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        await File.WriteAllTextAsync(path, json);
    }
}
