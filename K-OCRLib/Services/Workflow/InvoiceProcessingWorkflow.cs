using K_OCR.Models;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Orchestrates the full OCR processing pipeline for a single invoice file.
/// <para>
/// <see cref="AzureOcrStep"/> and <see cref="TesseractOcrStep"/> are executed
/// concurrently; all subsequent steps run sequentially in declared order.
/// </para>
/// </summary>
public class InvoiceProcessingWorkflow
{
    private readonly AzureOcrStep _azureOcrStep;
    private readonly TesseractOcrStep _tesseractOcrStep;
    private readonly TesseractValidationStep _tesseractValidationStep;
    private readonly LineItemValidationStep _lineItemValidationStep;
    private readonly ConfidenceValidationStep _confidenceValidationStep;
    private readonly EnrichmentStep _enrichmentStep;
    private readonly SaveContextStep _saveContextStep;
    private readonly ILogger<InvoiceProcessingWorkflow> _logger;

    public InvoiceProcessingWorkflow(
        AzureOcrStep azureOcrStep,
        TesseractOcrStep tesseractOcrStep,
        TesseractValidationStep tesseractValidationStep,
        LineItemValidationStep lineItemValidationStep,
        ConfidenceValidationStep confidenceValidationStep,
        EnrichmentStep enrichmentStep,
        SaveContextStep saveContextStep,
        ILogger<InvoiceProcessingWorkflow> logger)
    {
        _azureOcrStep = azureOcrStep;
        _tesseractOcrStep = tesseractOcrStep;
        _tesseractValidationStep = tesseractValidationStep;
        _lineItemValidationStep = lineItemValidationStep;
        _confidenceValidationStep = confidenceValidationStep;
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
    /// <param name="minConfidenceThreshold">
    /// Optional per-run confidence threshold override.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task<PipelineContext> RunAsync(
        string filePath,
        string? artifactsDirectory = null,
        double? minConfidenceThreshold = null,
        CancellationToken cancellationToken = default)
    {
        var context = new PipelineContext
        {
            InputPath = filePath,
            ArtifactsDirectory = artifactsDirectory,
            MinConfidenceThreshold = minConfidenceThreshold,
        };

        var fileName = Path.GetFileName(filePath);
        _logger.LogDebug("[Workflow] Starting pipeline for {File}.", fileName);

        // Azure OCR and Tesseract OCR run concurrently.
        await Task.WhenAll(
            _azureOcrStep.ExecuteAsync(context, cancellationToken),
            _tesseractOcrStep.ExecuteAsync(context, cancellationToken));

        // Remaining steps run sequentially in pipeline order.
        IWorkflowStep[] sequentialSteps =
        [
            _tesseractValidationStep,
            _lineItemValidationStep,
            _confidenceValidationStep,
            _enrichmentStep,
            _saveContextStep,
        ];

        foreach (var step in sequentialSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("[Workflow] Running step '{Step}' for {File}.", step.Name, fileName);
            await step.ExecuteAsync(context, cancellationToken);
        }

        _logger.LogDebug("[Workflow] Pipeline complete for {File}.", fileName);
        return context;
    }
}
