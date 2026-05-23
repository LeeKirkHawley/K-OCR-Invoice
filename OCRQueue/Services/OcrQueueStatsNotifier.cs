using System.Reactive.Linq;
using System.Reactive.Subjects;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Singleton Rx subject that emits a <see cref="QueueStats"/> snapshot whenever the
/// queue depth changes. Consumers (e.g. the SignalR bridge) subscribe to <see cref="Stats"/>.
/// </summary>
public sealed class OcrQueueStatsNotifier : IOcrQueueStatsNotifier, IDisposable
{
    private readonly Subject<QueueStats> _subject = new();

    public IObservable<QueueStats> Stats => _subject.AsObservable();

    public void Publish(QueueStats stats) => _subject.OnNext(stats);

    public void Dispose() => _subject.Dispose();
}
