using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public interface IOrganizationActivityLogService
{
    Task LogBatchCreatedAsync(string organizationName, string batchName, string orgUser);
    Task LogInvoiceUploadAsync(string organizationName, string batchName, string orgUser, IReadOnlyList<string> fileNames);
    Task LogBatchValidatedAsync(string organizationName, string batchName, string orgUser, string fileName);
    Task LogValidatedInvoiceDownloadAsync(string organizationName, string batchName, string orgUser, string fileName);
    Task LogBatchMarkedForDeletionAsync(string organizationName, string batchName, string orgUser);
    Task LogBatchDeletedAsync(string organizationName, string batchName, string orgUser);
}

public sealed class OrganizationActivityLogService : IOrganizationActivityLogService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPathService _pathService;
    private readonly ILogger<OrganizationActivityLogService> _logger;

    public OrganizationActivityLogService(IPathService pathService, ILogger<OrganizationActivityLogService> logger)
    {
        _pathService = pathService;
        _logger = logger;
    }

    public Task LogBatchCreatedAsync(string organizationName, string batchName, string orgUser) =>
        AppendAsync(organizationName, "BatchCreated", $"Batch=\"{batchName}\" User=\"{orgUser}\"");

    public Task LogInvoiceUploadAsync(string organizationName, string batchName, string orgUser, IReadOnlyList<string> fileNames) =>
        AppendAsync(organizationName, "InvoiceUpload", $"Batch=\"{batchName}\" User=\"{orgUser}\" Files=\"{string.Join(", ", fileNames)}\"");

    public Task LogBatchValidatedAsync(string organizationName, string batchName, string orgUser, string fileName) =>
        AppendAsync(organizationName, "BatchValidated", $"Batch=\"{batchName}\" User=\"{orgUser}\" Invoice=\"{fileName}\"");

    public Task LogValidatedInvoiceDownloadAsync(string organizationName, string batchName, string orgUser, string fileName) =>
        AppendAsync(organizationName, "ValidatedInvoiceDownload", $"Batch=\"{batchName}\" User=\"{orgUser}\" Invoice=\"{fileName}\"");

    public Task LogBatchMarkedForDeletionAsync(string organizationName, string batchName, string orgUser) =>
        AppendAsync(organizationName, "BatchMarkedForDeletion", $"Batch=\"{batchName}\" User=\"{orgUser}\"");

    public Task LogBatchDeletedAsync(string organizationName, string batchName, string orgUser) =>
        AppendAsync(organizationName, "BatchDeleted", $"Batch=\"{batchName}\" User=\"{orgUser}\"");

    private async Task AppendAsync(string organizationName, string eventName, string details)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            _logger.LogWarning("Skipping org activity log entry because organization name was empty: {EventName}", eventName);
            return;
        }

        var orgFolder = _pathService.GetOrgFolderPath(organizationName);
        var logPath = Path.Combine(orgFolder, "activity.log");
        var gate = Locks.GetOrAdd(logPath, _ => new SemaphoreSlim(1, 1));
        var acquired = false;

        try
        {
            Directory.CreateDirectory(orgFolder);
            await gate.WaitAsync();
            acquired = true;

            var line = $"{DateTime.UtcNow:O} | {eventName} | {details}{Environment.NewLine}";
            await File.AppendAllTextAsync(logPath, line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write org activity log for {OrganizationName} at {LogPath}", organizationName, logPath);
        }
        finally
        {
            if (acquired)
                gate.Release();
        }
    }
}
