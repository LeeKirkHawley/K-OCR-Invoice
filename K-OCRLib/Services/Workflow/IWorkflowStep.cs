using K_OCR.Identity;
using K_OCR.Models;

namespace K_OCR.Services.Workflow;

/// <summary>
/// A single, self-contained stage in the OCR processing pipeline.
/// </summary>
public interface IWorkflowStep
{
    /// <summary>Human-readable name used in logging.</summary>
    string Name { get; }

    /// <summary>
    /// Called before <see cref="ExecuteAsync"/>. Override to add per-organization
    /// setup logic (e.g. loading org-specific configuration or rules).
    /// </summary>
    Task PreRunAsync(Organization? organization, Batch? batch, PipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes this step. The step reads from and writes to
    /// <paramref name="context"/> to pass data to subsequent steps.
    /// </summary>
    Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after <see cref="ExecuteAsync"/> completes successfully. Override to add
    /// per-organization post-processing logic (e.g. auditing, notifications).
    /// </summary>
    Task PostRunAsync(Organization? organization, Batch? batch, PipelineContext context, CancellationToken cancellationToken = default);
}
