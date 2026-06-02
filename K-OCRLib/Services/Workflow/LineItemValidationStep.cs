using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that validates the mathematical accuracy of line items and
/// invoice totals for every invoice in the pipeline context.
/// </summary>
public class LineItemValidationStep : WorkflowStepBase
{
    private readonly ILineItemValidationService _validationService;

    public override string Name => "Line Item Validation";

    public LineItemValidationStep(ILineItemValidationService validationService)
    {
        _validationService = validationService;
    }

    public override Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        if (context.Layout == null)
            return Task.CompletedTask;

        foreach (var invoice in context.Layout)
            _validationService.ValidateInvoiceMath(invoice);

        return Task.CompletedTask;
    }
}
