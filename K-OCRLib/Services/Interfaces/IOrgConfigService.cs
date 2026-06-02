using K_OCRLib.Configuration;

namespace K_OCRLib.Services.Interfaces;

public interface IOrgConfigService
{
    /// <summary>
    /// Load the organisation configuration for <paramref name="orgName"/>.
    /// Returns defaults if OrgConfig.json does not yet exist.
    /// </summary>
    Task<OrgConfig> LoadAsync(string orgName);

    /// <summary>
    /// Persist the organisation configuration for <paramref name="orgName"/>.
    /// Creates the file if it does not exist.
    /// </summary>
    Task SaveAsync(string orgName, OrgConfig config);
}
