namespace K_OCR.Services;

public interface IPathService
{
    string BaseDirectory { get; }

    /// <summary>Strip characters that are not alphanumeric, underscore, or hyphen.</summary>
    string SanitizeName(string name);

    string GetOrgFolderPath(string orgName);
    /// <summary>Returns the full path to the per-org SQLite database file.</summary>
    string GetOrgDbPath(string orgName);
    /// <summary>Returns the full path to the per-org OrgConfig.json file.</summary>
    string GetOrgConfigPath(string orgName);
    string GetBatchFolderPath(string orgName, string batchName);
    string GetInvoicesFolderPath(string orgName, string batchName);
    string GetArtifactsFolderPath(string orgName, string batchName);
}
