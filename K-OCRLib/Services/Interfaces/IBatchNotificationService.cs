namespace K_OCRLib.Services.Interfaces;

public interface IBatchNotificationService
{
    /// <summary>
    /// Sends soft-delete notifications to all org admins. Uses the org's identity-DB ID.
    /// </summary>
    Task NotifyBatchSoftDeletedAsync(string orgId, string batchName, CancellationToken ct = default);

    /// <summary>
    /// Sends hard-delete notifications to all org admins.
    /// Uses the sanitized org folder name (as seen by the background cleanup service).
    /// </summary>
    Task NotifyBatchHardDeletedAsync(string orgSanitizedName, string batchName, CancellationToken ct = default);

    /// <summary>
    /// Sends restore notifications to all org admins. Uses the org's identity-DB ID.
    /// </summary>
    Task NotifyBatchRestoredAsync(string orgId, string batchName, CancellationToken ct = default);
}
