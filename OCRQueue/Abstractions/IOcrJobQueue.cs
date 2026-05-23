using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// In-memory queue façade. Backed by a <see cref="System.Threading.Channels.Channel{T}"/>
/// so that enqueue is non-blocking and the processor can pull jobs one at a time.
/// </summary>
public interface IOcrJobQueue
{
    Task EnqueueAsync(OcrJob job, CancellationToken ct = default);

    /// <summary>
    /// Waits for the next job and removes it from the queue. Blocks until a job is available
    /// or <paramref name="ct"/> is cancelled.
    /// </summary>
    ValueTask<OcrJob> DequeueAsync(CancellationToken ct = default);

    /// <summary>Number of jobs currently waiting in the channel.</summary>
    int TotalCount { get; }

    /// <summary>Per-org job counts keyed by org ID.</summary>
    IReadOnlyDictionary<string, int> CountPerOrg { get; }
}
