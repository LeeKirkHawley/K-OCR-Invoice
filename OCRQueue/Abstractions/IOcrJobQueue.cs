using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// In-memory queue façade. Backed by a <see cref="System.Threading.Channels.Channel{T}"/>
/// so that enqueue is non-blocking and the processor can pull jobs one at a time.
/// <para>
/// Counts represent <em>outstanding</em> jobs (enqueued but not yet completed),
/// which includes both jobs waiting in the channel and jobs currently being processed.
/// </para>
/// </summary>
public interface IOcrJobQueue
{
    Task EnqueueAsync(OcrJob job, CancellationToken ct = default);

    /// <summary>
    /// Waits for the next job and removes it from the channel. Blocks until a job is available
    /// or <paramref name="ct"/> is cancelled. Does NOT decrement the outstanding count —
    /// call <see cref="NotifyJobComplete"/> when the job finishes.
    /// </summary>
    ValueTask<OcrJob> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Decrements the outstanding count for the job's org and publishes updated stats.
    /// Must be called by the processor after every job completes (success or failure).
    /// </summary>
    void NotifyJobComplete(OcrJob job);

    /// <summary>Number of outstanding jobs (enqueued but not yet completed).</summary>
    int TotalCount { get; }

    /// <summary>Per-org outstanding job counts keyed by org ID.</summary>
    IReadOnlyDictionary<string, int> CountPerOrg { get; }

    /// <summary>Per-org invoice file names for all outstanding jobs, keyed by org ID.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> InvoicesPerOrg { get; }
}
