using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace K_OCR.Services;

public class PathService : IPathService
{
    public string BaseDirectory { get; }

    public PathService(IConfiguration configuration)
    {
        BaseDirectory = configuration["Kocr:BaseDirectory"] ?? string.Empty;
    }

    public string SanitizeName(string name) =>
        Regex.Replace(name ?? string.Empty, @"[^A-Za-z0-9_\-]", "_");

    public string GetOrgFolderPath(string orgName) =>
        Path.Combine(BaseDirectory, SanitizeName(orgName));

    public string GetBatchFolderPath(string orgName, string batchName) =>
        Path.Combine(GetOrgFolderPath(orgName), SanitizeName(batchName));

    public string GetInvoicesFolderPath(string orgName, string batchName) =>
        Path.Combine(GetBatchFolderPath(orgName, batchName), "Invoices");

    public string GetArtifactsFolderPath(string orgName, string batchName) =>
        Path.Combine(GetBatchFolderPath(orgName, batchName), "Artifacts");
}
