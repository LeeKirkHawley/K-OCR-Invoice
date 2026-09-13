using K_OCRLib.Identity;
using K_OCRLib.Models;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Orchestrates the full OCR processing pipeline for a single invoice file.
/// <para>
/// <see cref="AzureOcrStep"/> and <see cref="TesseractOcrStep"/> are executed
/// concurrently; all subsequent steps run sequentially in declared order.
/// For every step the sequence is: PreRunAsync → ExecuteAsync → PostRunAsync.
/// </para>
/// </summary>
public class InvoiceProcessingWorkflow
{
    private readonly AzureOcrStep _azureOcrStep;
    private readonly TesseractOcrStep _tesseractOcrStep;
    private readonly TesseractValidationStep _tesseractValidationStep;
    private readonly EnrichmentStep _enrichmentStep;
    private readonly SaveContextStep _saveContextStep;
    private readonly ILogger<InvoiceProcessingWorkflow> _logger;

    public InvoiceProcessingWorkflow(
        AzureOcrStep azureOcrStep,
        TesseractOcrStep tesseractOcrStep,
        TesseractValidationStep tesseractValidationStep,
        EnrichmentStep enrichmentStep,
        SaveContextStep saveContextStep,
        ILogger<InvoiceProcessingWorkflow> logger)
    {
        _azureOcrStep = azureOcrStep;
        _tesseractOcrStep = tesseractOcrStep;
        _tesseractValidationStep = tesseractValidationStep;
        _enrichmentStep = enrichmentStep;
        _saveContextStep = saveContextStep;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full pipeline for the given file and returns the populated
    /// <see cref="PipelineContext"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the invoice image or PDF.</param>
    /// <param name="artifactsDirectory">
    /// Optional directory for per-page PNG artifacts when the input is a PDF.
    /// </param>
    /// <param name="organization">
    /// The organization on whose behalf the pipeline is running. Passed to each
    /// step's pre-run and post-run hooks for per-organization customization.
    /// </param>
    /// <param name="batch">
    /// The batch being processed. Passed to each step's pre-run and post-run hooks.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task<PipelineContext> RunAsync(
        string filePath,
        string? artifactsDirectory = null,
        Organization? organization = null,
        Batch? batch = null,
        CancellationToken cancellationToken = default)
    {
        var context = new PipelineContext
        {
            InputPath = filePath,
            ArtifactsDirectory = artifactsDirectory,
            Organization = organization,
            Batch = batch,
        };

        var fileName = Path.GetFileName(filePath);
        _logger.LogDebug("[Workflow] Starting pipeline for {File}.", fileName);

        // Azure OCR and Tesseract OCR run concurrently, each with its own pre/post hooks.
        await Task.WhenAll(
            RunStepAsync(_azureOcrStep, organization, batch, context, cancellationToken),
            RunStepAsync(_tesseractOcrStep, organization, batch, context, cancellationToken)
        );

        // Remaining steps run sequentially in pipeline order.
        IWorkflowStep[] sequentialSteps =
        [
            _tesseractValidationStep,
            _enrichmentStep,
            _saveContextStep,
        ];

        foreach (var step in sequentialSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunStepAsync(step, organization, batch, context, cancellationToken);
        }

        _logger.LogDebug("[Workflow] Pipeline complete for {File}.", fileName);
        return context;
    }

    /// <summary>
    /// Executes a single step's full lifecycle: PreRunAsync → ExecuteAsync → PostRunAsync.
    /// </summary>
    private async Task RunStepAsync(
        IWorkflowStep step,
        Organization? organization,
        Batch? batch,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("[Workflow] Pre-run '{Step}' for {File}.", step.Name, Path.GetFileName(context.InputPath));
        await step.PreRunAsync(organization, batch, context, cancellationToken);

        _logger.LogDebug("[Workflow] Running '{Step}' for {File}.", step.Name, Path.GetFileName(context.InputPath));
        await step.ExecuteAsync(context, cancellationToken);

        _logger.LogDebug("[Workflow] Post-run '{Step}' for {File}.", step.Name, Path.GetFileName(context.InputPath));
        await step.PostRunAsync(organization, batch, context, cancellationToken);
    }
}
