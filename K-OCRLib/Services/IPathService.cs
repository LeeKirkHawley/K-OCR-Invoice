namespace K_OCR.Services;

public interface IPathService
{
    string BaseDirectory { get; }

    /// <summary>Strip characters that are not alphanumeric, underscore, or hyphen.</summary>
    string SanitizeName(string name);

    string GetOrgFolderPath(string orgName);
    string GetBatchFolderPath(string orgName, string batchName);
    string GetInvoicesFolderPath(string orgName, string batchName);
    string GetArtifactsFolderPath(string orgName, string batchName);
}
