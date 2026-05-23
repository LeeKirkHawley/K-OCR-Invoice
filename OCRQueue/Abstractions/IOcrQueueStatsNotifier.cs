using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// Publishes <see cref="QueueStats"/> snapshots via an Rx observable whenever the
/// queue depth changes. Consumed by the SignalR bridge hosted service.
/// </summary>
public interface IOcrQueueStatsNotifier
{
    IObservable<QueueStats> Stats { get; }

    /// <summary>
    /// Emit a new queue-depth snapshot. The caller (typically <see cref="IOcrJobQueue"/>)
    /// provides the current counts so the notifier has no dependency on the queue itself.
    /// </summary>
    void Publish(QueueStats stats);
}
