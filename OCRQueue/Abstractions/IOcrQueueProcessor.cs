namespace OCRQueue.Abstractions;

/// <summary>
/// Controls the lifecycle of the OCR job processing loop.
/// Use <see cref="PauseAndDrainAsync"/> to stop accepting new jobs while
/// allowing in-flight invoices to complete, then <see cref="Resume"/> to restart.
/// </summary>
public interface IOcrQueueProcessor
{
    /// <summary>True while the processor is paused (not picking up new jobs).</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Pauses the dequeue loop and waits for all currently-executing jobs to finish.
    /// Jobs still waiting in the channel remain persisted as <c>Queued</c> in the
    /// database and will be re-enqueued automatically on the next startup.
    /// </summary>
    Task PauseAndDrainAsync(CancellationToken ct = default);

    /// <summary>Resumes the dequeue loop after a <see cref="PauseAndDrainAsync"/> call.</summary>
    void Resume();
}
