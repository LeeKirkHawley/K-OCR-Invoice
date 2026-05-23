using OCRQueue.Models;

namespace OCRQueue.Abstractions;

/// <summary>
/// Implemented by each named OCR workflow. Register all implementations with DI;
/// <see cref="OcrWorkflowRegistry"/> resolves the correct one at runtime.
/// </summary>
public interface IQueuedOcrWorkflow
{
    /// <summary>
    /// Stable key that identifies this workflow (e.g. "Default", "TesseractOnly", "AzureOnly").
    /// Must match the value stored in <c>OrgConfig.OcrWorkflowKey</c>.
    /// </summary>
    string WorkflowKey { get; }

    Task ExecuteAsync(OcrJob job, CancellationToken ct);
}
