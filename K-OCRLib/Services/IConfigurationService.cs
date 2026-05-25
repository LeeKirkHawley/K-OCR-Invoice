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
    /// Returns the maximum number of active batches allowed for guest orgs from settings.
    /// </summary>
    int GetGuestMaxBatches();

    /// <summary>
    /// Returns the maximum number of pages per invoice for regular or guest users.
    /// Invoices exceeding this limit are grayed out and skipped during OCR.
    /// </summary>
    int GetMaxPagesPerInvoice(bool isGuest);

    /// <summary>
    /// Returns the maximum number of invoices allowed per batch for regular or guest users.
    /// </summary>
    int GetMaxInvoicesPerBatch(bool isGuest);
}
