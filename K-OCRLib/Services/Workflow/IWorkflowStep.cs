using K_OCR.Models;

namespace K_OCR.Services.Workflow;

/// <summary>
/// A single, self-contained stage in the OCR processing pipeline.
/// Pre-run and post-run hooks will be added in a future iteration.
/// </summary>
public interface IWorkflowStep
{
    /// <summary>Human-readable name used in logging.</summary>
    string Name { get; }

    /// <summary>
    /// Executes this step. The step reads from and writes to
    /// <paramref name="context"/> to pass data to subsequent steps.
    /// </summary>
    Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
