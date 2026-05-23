using K_OCR.Data;
using K_OCR.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Services;

/// <summary>
/// Reads and updates OCR job records in each org's per-org SQLite database.
/// Opens the database directly without tenant context so the processor can act
/// on behalf of any organisation.
/// </summary>
public sealed class OcrQueueRepository : IOcrQueueRepository
{
    private readonly K_OCR.Services.IPathService _pathService;
    private readonly ILogger<OcrQueueRepository> _logger;

    public OcrQueueRepository(
        K_OCR.Services.IPathService pathService,
        ILogger<OcrQueueRepository> logger)
    {
        _pathService = pathService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OcrJobEntity>> GetPendingJobsAsync(
        string orgName, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        return await db.OcrJobs
            .Where(j => j.Status == OcrJobStatus.Queued || j.Status == OcrJobStatus.Processing)
            .OrderBy(j => j.QueuedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<OcrJobEntity> CreateJobAsync(
        OcrJob job, string orgName, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        var entity = new OcrJobEntity
        {
            InvoiceId    = job.InvoiceId,
            BatchId      = job.BatchId,
            OrgId        = job.OrgId,
            OrgName      = orgName,
            FilePath     = job.FilePath,
            WorkflowKey  = job.WorkflowKey,
            Status       = OcrJobStatus.Queued,
            QueuedAtUtc  = job.QueuedAtUtc,
        };
        db.OcrJobs.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task MarkQueuedAsync(int jobId, string orgName, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        var job = await db.OcrJobs.FindAsync(new object?[] { jobId }, ct);
        if (job is null) return;
        job.Status        = OcrJobStatus.Queued;
        job.StartedAtUtc  = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkProcessingAsync(int jobId, string orgName, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        var job = await db.OcrJobs.FindAsync(new object?[] { jobId }, ct);
        if (job is null) return;
        job.Status       = OcrJobStatus.Processing;
        job.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkCompletedAsync(int jobId, string orgName, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        var job = await db.OcrJobs.FindAsync(new object?[] { jobId }, ct);
        if (job is null) return;
        job.Status         = OcrJobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(
        int jobId, string orgName, string error, CancellationToken ct = default)
    {
        await using var db = OpenOrgDb(orgName);
        var job = await db.OcrJobs.FindAsync(new object?[] { jobId }, ct);
        if (job is null) return;
        job.Status         = OcrJobStatus.Failed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.ErrorMessage   = error;
        await db.SaveChangesAsync(ct);
    }

    private KOCRDbContext OpenOrgDb(string orgName)
    {
        var dbPath = _pathService.GetOrgDbPath(orgName);
        var opts = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new KOCRDbContext(opts);
        ctx.Database.Migrate();
        return ctx;
    }
}
