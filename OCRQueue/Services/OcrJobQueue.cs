using System.Collections.Concurrent;
using System.Threading.Channels;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Singleton in-memory queue backed by a <see cref="Channel{T}"/>.
/// Maintains per-org job counts and publishes a <see cref="QueueStats"/> snapshot
/// via <see cref="IOcrQueueStatsNotifier"/> on every enqueue and dequeue.
/// </summary>
public sealed class OcrJobQueue : IOcrJobQueue, IDisposable
{
    private readonly Channel<OcrJob> _channel =
        Channel.CreateUnbounded<OcrJob>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

    private readonly ConcurrentDictionary<string, int> _perOrgCounts = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _perOrgJobs = new();
    private readonly IOcrQueueStatsNotifier _statsNotifier;
    private int _totalCount;

    public OcrJobQueue(IOcrQueueStatsNotifier statsNotifier)
    {
        _statsNotifier = statsNotifier;
    }

    public int TotalCount => _totalCount;

    public IReadOnlyDictionary<string, int> CountPerOrg => _perOrgCounts;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> InvoicesPerOrg =>
        _perOrgJobs.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.Values.ToList());

    public async Task EnqueueAsync(OcrJob job, CancellationToken ct = default)
    {
        _perOrgCounts.AddOrUpdate(job.OrgId, 1, (_, v) => v + 1);
        _perOrgJobs.GetOrAdd(job.OrgId, _ => new ConcurrentDictionary<int, string>())
                   .TryAdd(job.JobId, Path.GetFileName(job.FilePath));
        Interlocked.Increment(ref _totalCount);
        // Publish before writing to the channel so subscribers see the non-zero count
        // before the processor thread can pick up the job.
        PublishStats();
        await _channel.Writer.WriteAsync(job, ct);
    }

    public async ValueTask<OcrJob> DequeueAsync(CancellationToken ct = default)
    {
        // Count is NOT decremented here — the job is still outstanding until it completes.
        // Call NotifyJobComplete after processing finishes.
        return await _channel.Reader.ReadAsync(ct);
    }

    public void NotifyJobComplete(OcrJob job)
    {
        // Remove from per-org file tracking.
        if (_perOrgJobs.TryGetValue(job.OrgId, out var jobFiles))
        {
            jobFiles.TryRemove(job.JobId, out _);
            if (jobFiles.IsEmpty)
                _perOrgJobs.TryRemove(job.OrgId, out _);
        }

        // Atomically decrement; if it reaches zero, remove the key so the org
        // disappears from stats rather than showing a persistent zero-count row.
        var newCount = _perOrgCounts.AddOrUpdate(job.OrgId, 0, (_, v) => Math.Max(0, v - 1));
        if (newCount == 0)
            _perOrgCounts.TryRemove(new KeyValuePair<string, int>(job.OrgId, 0));

        var newTotal = Interlocked.Decrement(ref _totalCount);
        if (newTotal < 0) Interlocked.Exchange(ref _totalCount, 0);
        PublishStats();
    }

    private void PublishStats()
    {
        var perOrgInvoices = _perOrgJobs.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.Values.ToList());

        _statsNotifier.Publish(new QueueStats(
            _totalCount,
            new Dictionary<string, int>(_perOrgCounts),
            perOrgInvoices));
    }

    public void Dispose() => _channel.Writer.TryComplete();
}
