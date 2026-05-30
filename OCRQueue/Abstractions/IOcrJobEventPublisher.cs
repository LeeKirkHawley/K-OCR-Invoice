using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// Publishes job-lifecycle events as Rx observables so downstream consumers
/// (e.g. the SignalR bridge in the host) can react to completions and failures
/// without a direct dependency on <c>OcrQueueProcessor</c>.
/// </summary>
public interface IOcrJobEventPublisher
{
    /// <summary>Stream that emits a <see cref="OcrJob"/> each time a job is enqueued.</summary>
    IObservable<OcrJob> JobEnqueued { get; }

    /// <summary>Stream that emits a <see cref="OcrJob"/> each time a job completes successfully.</summary>
    IObservable<OcrJob> JobCompleted { get; }

    /// <summary>Stream that emits a <see cref="OcrJobFailedEvent"/> each time a job fails.</summary>
    IObservable<OcrJobFailedEvent> JobFailed { get; }

    /// <summary>Called by <c>OcrEnqueueService</c> after a job is durably queued.</summary>
    void OnJobEnqueued(OcrJob job);

    /// <summary>Called by <c>OcrQueueProcessor</c> after a workflow finishes successfully.</summary>
    void OnJobCompleted(OcrJob job);

    /// <summary>Called by <c>OcrQueueProcessor</c> after a workflow throws.</summary>
    void OnJobFailed(OcrJob job, string error);
}

/// <summary>Carries a failed <see cref="OcrJob"/> and its error message.</summary>
public record OcrJobFailedEvent(OcrJob Job, string Error);
