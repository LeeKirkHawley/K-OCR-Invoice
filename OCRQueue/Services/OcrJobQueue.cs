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
    private readonly IOcrQueueStatsNotifier _statsNotifier;
    private int _totalCount;

    public OcrJobQueue(IOcrQueueStatsNotifier statsNotifier)
    {
        _statsNotifier = statsNotifier;
    }

    public int TotalCount => _totalCount;

    public IReadOnlyDictionary<string, int> CountPerOrg => _perOrgCounts;

    public async Task EnqueueAsync(OcrJob job, CancellationToken ct = default)
    {
        _perOrgCounts.AddOrUpdate(job.OrgId, 1, (_, v) => v + 1);
        Interlocked.Increment(ref _totalCount);
        await _channel.Writer.WriteAsync(job, ct);
        PublishStats();
    }

    public async ValueTask<OcrJob> DequeueAsync(CancellationToken ct = default)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        _perOrgCounts.AddOrUpdate(job.OrgId, 0, (_, v) => Math.Max(0, v - 1));
        Interlocked.Decrement(ref _totalCount);
        PublishStats();
        return job;
    }

    private void PublishStats() =>
        _statsNotifier.Publish(new QueueStats(_totalCount, new Dictionary<string, int>(_perOrgCounts)));

    public void Dispose() => _channel.Writer.TryComplete();
}
