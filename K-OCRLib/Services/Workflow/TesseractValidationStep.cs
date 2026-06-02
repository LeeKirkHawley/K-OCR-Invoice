using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that cross-validates every Azure-extracted invoice field
/// against the raw Tesseract OCR text stored in the pipeline context.
/// <para>
/// Skipped entirely when <see cref="PipelineContext.TesseractOcrText"/> is
/// <c>null</c>, which indicates a Tesseract infrastructure failure.
/// </para>
/// </summary>
public class TesseractValidationStep : WorkflowStepBase
{
    private readonly IInvoiceValidationService _validationService;

    public override string Name => "Tesseract Validation";

    public TesseractValidationStep(IInvoiceValidationService validationService)
    {
        _validationService = validationService;
    }

    public override Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        // null  = Tesseract task threw (infrastructure failure) → skip validation
        // ""    = Tesseract ran but found no text → FlagAllExtracted inside service
        // other = normal path
        if (context.TesseractOcrText == null || context.Layout == null)
            return Task.CompletedTask;

        foreach (var invoice in context.Layout)
            _validationService.ValidateAgainstTesseract(invoice, context.TesseractOcrText);

        return Task.CompletedTask;
    }
}
