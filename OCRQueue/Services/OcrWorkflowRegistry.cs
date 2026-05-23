using OCRQueue.Abstractions;

namespace OCRQueue.Services;

/// <summary>
/// Resolves the <see cref="IQueuedOcrWorkflow"/> for a given workflow key.
/// All registered workflows are injected via <see cref="IEnumerable{T}"/>;
/// an unrecognised key falls back to the "Default" workflow.
/// </summary>
public sealed class OcrWorkflowRegistry
{
    private readonly IReadOnlyDictionary<string, IQueuedOcrWorkflow> _workflows;

    public OcrWorkflowRegistry(IEnumerable<IQueuedOcrWorkflow> workflows)
    {
        _workflows = workflows.ToDictionary(
            w => w.WorkflowKey,
            StringComparer.OrdinalIgnoreCase);
    }

    public IQueuedOcrWorkflow Resolve(string key)
    {
        if (_workflows.TryGetValue(key, out var workflow))
            return workflow;

        if (_workflows.TryGetValue("Default", out var fallback))
            return fallback;

        throw new InvalidOperationException(
            $"No OCR workflow registered for key '{key}' and no 'Default' fallback exists.");
    }

    public IReadOnlyCollection<string> RegisteredKeys => _workflows.Keys.ToArray();
}
