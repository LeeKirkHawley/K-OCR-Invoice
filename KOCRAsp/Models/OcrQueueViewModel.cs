namespace KOCRAsp.Models;

/// <summary>
/// View model for the Admin OCR Queue monitoring page.
/// The initial snapshot is server-rendered; subsequent updates arrive via SignalR.
/// </summary>
public class OcrQueueViewModel
{
    /// <summary>Total number of jobs currently in the queue (all orgs).</summary>
    public int TotalQueued { get; init; }

    /// <summary>Per-org job counts. Key is org ID (GUID string), value is queue depth.</summary>
    public IReadOnlyDictionary<string, int> PerOrgCount { get; init; } =
        new Dictionary<string, int>();

    /// <summary>
    /// Org ID → org name mapping, resolved from the identity database.
    /// Used to display friendly names instead of GUIDs.
    /// </summary>
    public IReadOnlyDictionary<string, string> OrgNames { get; init; } =
        new Dictionary<string, string>();
}
