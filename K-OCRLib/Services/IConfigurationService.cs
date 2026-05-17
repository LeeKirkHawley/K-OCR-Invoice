using K_OCR.Configuration;

namespace K_OCR.Services;

public interface IConfigurationService
{
    /// <summary>
    /// Load application settings from the specified path or default location
    /// </summary>
    Task<AppSettings> LoadSettingsAsync(string? path = null);
    
    /// <summary>
    /// Save application settings to the specified path or default location
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings, string? path = null);
    
    /// <summary>
    /// Get default application settings
    /// </summary>
    AppSettings GetDefaultSettings();
    
    /// <summary>
    /// Get the default settings file path
    /// </summary>
    string GetDefaultSettingsPath();

    /// <summary>
    /// Returns the guest-org OCR page limit from settings. Synchronous for use in request pipelines.
    /// </summary>
    int GetGuestOcrPageLimit();
}
