using KOCRAsp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace KOCRAsp.Services;

/// <summary>
/// Singleton hosted service that bridges the Rx.NET OCR event streams to SignalR,
/// so clients receive real-time updates without polling.
/// <list type="bullet">
///   <item>
///     Subscribes to <see cref="IOcrQueueStatsNotifier.Stats"/> and pushes
///     <c>QueueStatsUpdated</c> to the <c>QueueMonitors</c> SignalR group.
///   </item>
///   <item>
///     Subscribes to job lifecycle streams and pushes per-org messages:
///     <c>InvoiceOcrQueued</c>, <c>InvoiceOcrUnqueued</c>, and <c>InvoiceOcrCompleted</c>.
///   </item>
/// </list>
/// </summary>
public sealed class OcrSignalRBridge : IHostedService, IDisposable
{
    private readonly IHubContext<OcrHub> _hubContext;
    private readonly IOcrQueueStatsNotifier _statsNotifier;
    private readonly IOcrJobEventPublisher _eventPublisher;
    private readonly ILogger<OcrSignalRBridge> _logger;

    private IDisposable? _statsSubscription;
    private IDisposable? _enqueuedSubscription;
    private IDisposable? _completedSubscription;
    private IDisposable? _failedSubscription;

    public OcrSignalRBridge(
        IHubContext<OcrHub> hubContext,
        IOcrQueueStatsNotifier statsNotifier,
        IOcrJobEventPublisher eventPublisher,
        ILogger<OcrSignalRBridge> logger)
    {
        _hubContext     = hubContext;
        _statsNotifier  = statsNotifier;
        _eventPublisher = eventPublisher;
        _logger         = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statsSubscription = _statsNotifier.Stats.Subscribe(
            onNext:  stats => OnStatsChanged(stats),
            onError: ex    => _logger.LogError(ex, "[OcrSignalR] Stats stream faulted."));

        _enqueuedSubscription = _eventPublisher.JobEnqueued.Subscribe(
            onNext:  job => OnJobEnqueued(job),
            onError: ex  => _logger.LogError(ex, "[OcrSignalR] JobEnqueued stream faulted."));

        _completedSubscription = _eventPublisher.JobCompleted.Subscribe(
            onNext:  job => OnJobCompleted(job),
            onError: ex  => _logger.LogError(ex, "[OcrSignalR] JobCompleted stream faulted."));

        _failedSubscription = _eventPublisher.JobFailed.Subscribe(
            onNext:  evt => OnJobFailed(evt),
            onError: ex  => _logger.LogError(ex, "[OcrSignalR] JobFailed stream faulted."));

        _logger.LogInformation("[OcrSignalR] Bridge started — subscribed to Rx streams.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _statsSubscription?.Dispose();
        _enqueuedSubscription?.Dispose();
        _completedSubscription?.Dispose();
        _failedSubscription?.Dispose();
        _logger.LogInformation("[OcrSignalR] Bridge stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _statsSubscription?.Dispose();
        _enqueuedSubscription?.Dispose();
        _completedSubscription?.Dispose();
        _failedSubscription?.Dispose();
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnStatsChanged(QueueStats stats)
    {
        var payload = new
        {
            totalQueued    = stats.TotalQueued,
            perOrgCount    = stats.PerOrgCount,
            perOrgInvoices = stats.PerOrgInvoices,
        };

        // Fire-and-forget: hub SendAsync is thread-safe but async.
        _ = _hubContext.Clients
            .Group("QueueMonitors")
            .SendAsync("QueueStatsUpdated", payload);
    }

    private void OnJobEnqueued(OcrJob job)
    {
        var payload = new
        {
            invoiceId = job.InvoiceId,
            batchId   = job.BatchId,
            jobId     = job.JobId,
            filePath  = job.FilePath,
        };

        _ = _hubContext.Clients
            .Group($"OcrCompleted_{job.OrgId}")
            .SendAsync("InvoiceOcrQueued", payload);
    }

    private void OnJobCompleted(OcrJob job)
    {
        OnJobUnqueued(job, "Completed");

        var payload = new
        {
            invoiceId = job.InvoiceId,
            batchId   = job.BatchId,
            jobId     = job.JobId,
            filePath  = job.FilePath,
        };

        _ = _hubContext.Clients
            .Group($"OcrCompleted_{job.OrgId}")
            .SendAsync("InvoiceOcrCompleted", payload);

        _logger.LogDebug(
            "[OcrSignalR] Pushed InvoiceOcrCompleted — job {JobId}, invoice {InvoiceId}, org {OrgId}.",
            job.JobId, job.InvoiceId, job.OrgId);
    }

    private void OnJobFailed(OcrJobFailedEvent failedEvent) =>
        OnJobUnqueued(failedEvent.Job, "Failed");

    private void OnJobUnqueued(OcrJob job, string reason)
    {
        var payload = new
        {
            invoiceId = job.InvoiceId,
            batchId   = job.BatchId,
            jobId     = job.JobId,
            filePath  = job.FilePath,
            reason,
        };

        _ = _hubContext.Clients
            .Group($"OcrCompleted_{job.OrgId}")
            .SendAsync("InvoiceOcrUnqueued", payload);
    }
}
