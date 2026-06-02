using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that validates Azure Document Intelligence confidence scores
/// for every extracted invoice field.
/// <para>
/// The threshold is resolved in priority order:
/// <list type="number">
///   <item><see cref="PipelineContext.MinConfidenceThreshold"/> (per-run override)</item>
///   <item><c>MinConfidenceThreshold</c> configuration key</item>
///   <item>Hard-coded default of <c>0.8</c></item>
/// </list>
/// </para>
/// </summary>
public class ConfidenceValidationStep : WorkflowStepBase
{
    private readonly IConfidenceValidationService _validationService;
    private readonly IConfiguration _configuration;

    public override string Name => "Confidence Validation";

    public ConfidenceValidationStep(
        IConfidenceValidationService validationService,
        IConfiguration configuration)
    {
        _validationService = validationService;
        _configuration = configuration;
    }

    public override Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        if (context.Layout == null)
            return Task.CompletedTask;

        var threshold = context.MinConfidenceThreshold
            ?? _configuration.GetValue<double>("MinConfidenceThreshold", 0.8);

        foreach (var invoice in context.Layout)
            _validationService.ValidateConfidence(invoice, threshold);

        return Task.CompletedTask;
    }
}
