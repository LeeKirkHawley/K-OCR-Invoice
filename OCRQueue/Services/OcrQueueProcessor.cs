using System.Threading.RateLimiting;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Singleton background service that drives the OCR job pipeline.
/// <list type="bullet">
///   <item>On startup, scans every org's SQLite database for interrupted (Processing)
///   and pending (Queued) jobs and re-enqueues them.</item>
///   <item>Dequeues jobs one at a time, acquires a rate-limiter lease, then dispatches
///   them concurrently up to <see cref="OcrQueueSettings.MaxConcurrentJobs"/>.</item>
/// </list>
/// </summary>
public sealed class OcrQueueProcessor : BackgroundService
{
    private readonly IOcrJobQueue _jobQueue;
    private readonly IOcrQueueRepository _repository;
    private readonly OcrWorkflowRegistry _workflowRegistry;
    private readonly IOcrJobEventPublisher _eventPublisher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPathService _pathService;
    private readonly OcrQueueSettings _settings;
    private readonly ILogger<OcrQueueProcessor> _logger;
    private readonly RateLimiter _rateLimiter;

    public OcrQueueProcessor(
        IOcrJobQueue jobQueue,
        IOcrQueueRepository repository,
        OcrWorkflowRegistry workflowRegistry,
        IOcrJobEventPublisher eventPublisher,
        IServiceScopeFactory scopeFactory,
        IPathService pathService,
        IOptions<OcrQueueSettings> settings,
        ILogger<OcrQueueProcessor> logger)
    {
        _jobQueue         = jobQueue;
        _repository       = repository;
        _workflowRegistry = workflowRegistry;
        _eventPublisher   = eventPublisher;
        _scopeFactory     = scopeFactory;
        _pathService      = pathService;
        _settings         = settings.Value;
        _logger           = logger;

        _rateLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit          = _settings.MaxJobsPerWindow,
            Window               = TimeSpan.FromSeconds(_settings.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = int.MaxValue,
            AutoReplenishment    = true,
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ScanAndRequeueAsync(stoppingToken);
        await ProcessLoopAsync(stoppingToken);
    }

    /// <summary>
    /// Enumerates all org databases on startup and re-enqueues any jobs that were
    /// in-flight or pending when the application last shut down.
    /// </summary>
    private async Task ScanAndRequeueAsync(CancellationToken ct)
    {
        _logger.LogInformation("[OcrQueue] Scanning org databases for pending jobs on startup.");

        List<(string Id, string Name)> orgs;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows  = await appDb.Organizations
                .Select(o => new { o.Id, o.Name })
                .ToListAsync(ct);
            orgs = rows.Select(o => (o.Id, o.Name)).ToList();
        }

        var requeued = 0;
        foreach (var (orgId, orgName) in orgs)
        {
            var dbPath = _pathService.GetOrgDbPath(orgName);
            if (!File.Exists(dbPath))
                continue;

            try
            {
                var pending = await _repository.GetPendingJobsAsync(orgName, ct);
                foreach (var entity in pending)
                {
                    // Jobs left in Processing state were interrupted by an app restart —
                    // reset them so they can be safely re-processed.
                    if (entity.Status == OcrJobStatus.Processing)
                        await _repository.MarkQueuedAsync(entity.Id, orgName, ct);

                    var job = EntityToJob(entity, orgId);
                    await _jobQueue.EnqueueAsync(job, ct);
                    requeued++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[OcrQueue] Could not scan pending jobs for org {OrgName}.", orgName);
            }
        }

        _logger.LogInformation(
            "[OcrQueue] Startup scan complete — {Count} job(s) re-enqueued.", requeued);
    }

    /// <summary>
    /// Main processing loop. Dequeues jobs, acquires rate-limiter leases, and runs
    /// them concurrently up to <see cref="OcrQueueSettings.MaxConcurrentJobs"/>.
    /// </summary>
    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        using var concurrencySemaphore = new SemaphoreSlim(
            _settings.MaxConcurrentJobs, _settings.MaxConcurrentJobs);

        while (!ct.IsCancellationRequested)
        {
            OcrJob job;
            try
            {
                job = await _jobQueue.DequeueAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            // Acquire a rate-limiter lease before starting the job.
            // AcquireAsync waits until the current window has capacity.
            using var lease = await _rateLimiter.AcquireAsync(permitCount: 1, ct);
            if (!lease.IsAcquired)
            {
                _logger.LogWarning(
                    "[OcrQueue] Rate-limiter denied lease for job {JobId}; job will be lost.",
                    job.JobId);
                continue;
            }

            // Acquire a concurrency slot.
            await concurrencySemaphore.WaitAsync(ct);

            // Fire-and-forget within the bounded semaphore so we can continue dequeueing.
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJobAsync(job, ct);
                }
                finally
                {
                    concurrencySemaphore.Release();
                }
            }, ct);
        }

        // Wait for in-flight jobs to finish before returning.
        for (var i = 0; i < _settings.MaxConcurrentJobs; i++)
            await concurrencySemaphore.WaitAsync(CancellationToken.None);
    }

    private async Task ProcessJobAsync(OcrJob job, CancellationToken ct)
    {
        _logger.LogInformation(
            "[OcrQueue] Starting job {JobId} — org: {OrgName}, invoice: {InvoiceId}, workflow: {Workflow}.",
            job.JobId, job.OrgName, job.InvoiceId, job.WorkflowKey);

        await _repository.MarkProcessingAsync(job.JobId, job.OrgName, ct);

        try
        {
            var workflow = _workflowRegistry.Resolve(job.WorkflowKey);
            await workflow.ExecuteAsync(job, ct);
            await _repository.MarkCompletedAsync(job.JobId, job.OrgName, ct);
            _eventPublisher.OnJobCompleted(job);

            _logger.LogInformation(
                "[OcrQueue] Job {JobId} completed — org: {OrgName}, invoice: {InvoiceId}.",
                job.JobId, job.OrgName, job.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OcrQueue] Job {JobId} failed — org: {OrgName}, invoice: {InvoiceId}.",
                job.JobId, job.OrgName, job.InvoiceId);

            _eventPublisher.OnJobFailed(job, ex.Message);
            await _repository.MarkFailedAsync(job.JobId, job.OrgName, ex.Message, CancellationToken.None);
        }
    }

    private static OcrJob EntityToJob(OcrJobEntity entity, string orgId) =>
        new(
            JobId:       entity.Id,
            InvoiceId:   entity.InvoiceId ?? 0,
            BatchId:     entity.BatchId,
            OrgId:       orgId,
            OrgName:     entity.OrgName,
            FilePath:    entity.FilePath,
            WorkflowKey: entity.WorkflowKey,
            QueuedAtUtc: entity.QueuedAtUtc);

    public override void Dispose()
    {
        _rateLimiter.Dispose();
        base.Dispose();
    }
}
