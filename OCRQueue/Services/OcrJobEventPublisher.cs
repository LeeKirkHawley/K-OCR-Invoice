using System.Reactive.Linq;
using System.Reactive.Subjects;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Singleton implementation of <see cref="IOcrJobEventPublisher"/>.
/// Uses hot Rx <see cref="Subject{T}"/> instances so any number of subscribers
/// (e.g. the SignalR bridge) receive every event.
/// </summary>
public sealed class OcrJobEventPublisher : IOcrJobEventPublisher, IDisposable
{
    private readonly Subject<OcrJob> _enqueued = new();
    private readonly Subject<OcrJob> _completed = new();
    private readonly Subject<OcrJobFailedEvent> _failed = new();

    /// <inheritdoc/>
    public IObservable<OcrJob> JobEnqueued => _enqueued.AsObservable();

    /// <inheritdoc/>
    public IObservable<OcrJob> JobCompleted => _completed.AsObservable();

    /// <inheritdoc/>
    public IObservable<OcrJobFailedEvent> JobFailed => _failed.AsObservable();

    /// <inheritdoc/>
    public void OnJobEnqueued(OcrJob job) => _enqueued.OnNext(job);

    /// <inheritdoc/>
    public void OnJobCompleted(OcrJob job) => _completed.OnNext(job);

    /// <inheritdoc/>
    public void OnJobFailed(OcrJob job, string error) =>
        _failed.OnNext(new OcrJobFailedEvent(job, error));

    public void Dispose()
    {
        _enqueued.OnCompleted();
        _enqueued.Dispose();
        _completed.OnCompleted();
        _completed.Dispose();
        _failed.OnCompleted();
        _failed.Dispose();
    }
}
