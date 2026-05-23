namespace OCRQueue.Models;

/// <summary>
/// Point-in-time snapshot of queue depth, published via Rx whenever the queue changes.
/// </summary>
public record QueueStats(
    int TotalQueued,
    IReadOnlyDictionary<string, int> PerOrgCount,
    IReadOnlyDictionary<string, IReadOnlyList<string>> PerOrgInvoices);
