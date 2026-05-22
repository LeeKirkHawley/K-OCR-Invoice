using K_OCR.Models;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that calls Azure Document Intelligence to extract structured
/// invoice data. Writes the result to <see cref="PipelineContext.Layout"/>.
/// </summary>
public class AzureOcrStep : WorkflowStepBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<AzureOcrStep> _logger;

    public override string Name => "Azure OCR";

    public AzureOcrStep(IInvoiceService invoiceService, ILogger<AzureOcrStep> logger)
    {
        _invoiceService = invoiceService;
        _logger = logger;
    }

    public override async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[{Step}] Starting for {File}.", Name, Path.GetFileName(context.InputPath));
        context.Layout = await _invoiceService.RunAzureInvoiceParse(context.InputPath);
        _logger.LogDebug("[{Step}] Completed for {File}.", Name, Path.GetFileName(context.InputPath));
    }
}
