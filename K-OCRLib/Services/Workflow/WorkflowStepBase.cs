using K_OCR.Identity;
using K_OCR.Models;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Base class for workflow steps. Provides no-op default implementations of
/// <see cref="PreRunAsync"/> and <see cref="PostRunAsync"/> so that concrete
/// steps only override the hooks they need.
/// </summary>
public abstract class WorkflowStepBase : IWorkflowStep
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public virtual Task PreRunAsync(
        Organization? organization,
        Batch? batch,
        PipelineContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public abstract Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual Task PostRunAsync(
        Organization? organization,
        Batch? batch,
        PipelineContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
