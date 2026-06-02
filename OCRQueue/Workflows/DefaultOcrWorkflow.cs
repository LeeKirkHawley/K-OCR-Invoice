using K_OCR.Services.Workflow;
using K_OCRLib.Data;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Workflows;

/// <summary>
/// The default OCR workflow — wraps the existing <see cref="InvoiceProcessingWorkflow"/>
/// (Azure + Tesseract concurrent, validation, enrichment, save).
/// <para>
/// Because <see cref="InvoiceProcessingWorkflow"/> and its steps are registered as
/// scoped services (they depend on a per-org EF context), this workflow creates an
/// explicit DI scope per job and manually constructs the DB-dependent services with
/// a <see cref="DirectDbContextFactory"/> that opens the org's SQLite directly without
/// requiring an active <c>ITenantContext</c>.
/// </para>
/// </summary>
public sealed class DefaultOcrWorkflow : IQueuedOcrWorkflow
{
    public string WorkflowKey => "Default";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPathService _pathService;
    private readonly ILoggerFactory _loggerFactory;

    public DefaultOcrWorkflow(
        IServiceScopeFactory scopeFactory,
        IPathService pathService,
        ILoggerFactory loggerFactory)
    {
        _scopeFactory  = scopeFactory;
        _pathService   = pathService;
        _loggerFactory = loggerFactory;
    }

    public async Task ExecuteAsync(OcrJob job, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Load the Organization from the shared application-level database.
        var appDb = sp.GetRequiredService<ApplicationDbContext>();
        var org   = await appDb.Organizations.FindAsync(new object?[] { job.OrgId }, ct);
        if (org is null)
        {
            _loggerFactory.CreateLogger<DefaultOcrWorkflow>()
                .LogError("[DefaultOcrWorkflow] Organization {OrgId} not found; skipping job {JobId}.",
                    job.OrgId, job.JobId);
            throw new InvalidOperationException(
                $"Organization '{job.OrgId}' not found in the application database.");
        }

        // Open the org's per-tenant SQLite directly — avoids the ITenantContext dependency.
        var dbPath = _pathService.GetOrgDbPath(job.OrgName);
        await using var orgCtx = OpenOrgDb(dbPath);
        var batch = await orgCtx.Batches.FindAsync(new object?[] { job.BatchId }, ct);
        if (batch is null)
        {
            _loggerFactory.CreateLogger<DefaultOcrWorkflow>()
                .LogError("[DefaultOcrWorkflow] Batch {BatchId} not found for job {JobId}.",
                    job.BatchId, job.JobId);
            throw new InvalidOperationException(
                $"Batch '{job.BatchId}' not found in org '{job.OrgName}' database.");
        }

        var artifactsDir = Path.Combine(batch.FolderPath, "Artifacts");

        // Manually construct the DB-dependent services using a direct factory so that
        // the scoped OrgDbContextFactory (which requires ITenantContext) is never invoked.
        var directFactory = new DirectDbContextFactory(dbPath);
        var dbService     = new DatabaseService(directFactory, _loggerFactory.CreateLogger<DatabaseService>());
        var fileService   = new FileService(dbService, _loggerFactory.CreateLogger<FileService>());
        var saveStep      = new SaveContextStep(fileService);

        // All remaining steps depend only on singleton services and are safe to resolve from scope.
        var azureStep  = sp.GetRequiredService<AzureOcrStep>();
        var tessStep   = sp.GetRequiredService<TesseractOcrStep>();
        var tessVal    = sp.GetRequiredService<TesseractValidationStep>();
        var lineVal    = sp.GetRequiredService<LineItemValidationStep>();
        var confVal    = sp.GetRequiredService<ConfidenceValidationStep>();
        var enrichStep = sp.GetRequiredService<EnrichmentStep>();

        var workflow = new InvoiceProcessingWorkflow(
            azureStep, tessStep, tessVal, lineVal, confVal, enrichStep, saveStep,
            _loggerFactory.CreateLogger<InvoiceProcessingWorkflow>());

        await workflow.RunAsync(
            filePath:          job.FilePath,
            artifactsDirectory: artifactsDir,
            organization:      org,
            batch:             batch,
            cancellationToken: ct);
    }

    private static KOCRDbContext OpenOrgDb(string dbPath)
    {
        var opts = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new KOCRDbContext(opts);
        ctx.Database.Migrate();
        return ctx;
    }
}
