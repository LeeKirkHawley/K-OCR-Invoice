using K_OCR.Models;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that runs local Tesseract OCR as a secondary extraction pass.
/// Writes the extracted text to <see cref="PipelineContext.TesseractOcrText"/>.
/// <para>
/// A Tesseract failure is logged but does not abort the pipeline — downstream
/// steps that read <see cref="PipelineContext.TesseractOcrText"/> treat
/// <c>null</c> as "infrastructure failure; skip Tesseract validation".
/// </para>
/// </summary>
public class TesseractOcrStep : WorkflowStepBase
{
    private readonly ITesseractValidationService _tesseractService;
    private readonly ILogger<TesseractOcrStep> _logger;

    public override string Name => "Tesseract OCR";

    public TesseractOcrStep(ITesseractValidationService tesseractService, ILogger<TesseractOcrStep> logger)
    {
        _tesseractService = tesseractService;
        _logger = logger;
    }

    public override async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[{Step}] Starting for {File}.", Name, Path.GetFileName(context.InputPath));
        try
        {
            context.TesseractOcrText = await _tesseractService.ExtractTextAsync(
                context.InputPath, context.ArtifactsDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            // Leave TesseractOcrText = null so downstream steps can distinguish
            // an infrastructure failure from "ran but produced no text" (empty string).
            _logger.LogError(ex, "[{Step}] Failed for {File}.", Name, Path.GetFileName(context.InputPath));
        }

        _logger.LogDebug("[{Step}] Completed for {File}.", Name, Path.GetFileName(context.InputPath));
    }
}
