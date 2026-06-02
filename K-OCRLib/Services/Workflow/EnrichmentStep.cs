using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that enriches every invoice in the pipeline context with
/// the vendor's country of origin and the invoice currency code.
/// </summary>
public class EnrichmentStep : WorkflowStepBase
{
    private readonly IInvoiceEnrichmentService _enrichmentService;

    public override string Name => "Enrichment";

    public EnrichmentStep(IInvoiceEnrichmentService enrichmentService)
    {
        _enrichmentService = enrichmentService;
    }

    public override Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        if (context.Layout == null)
            return Task.CompletedTask;

        foreach (var invoice in context.Layout)
            _enrichmentService.DetectCountryAndCurrency(invoice, context.TesseractOcrText);

        return Task.CompletedTask;
    }
}
