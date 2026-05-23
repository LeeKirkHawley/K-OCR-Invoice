using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Workflows;

/// <summary>Stub: Azure-only OCR workflow (not yet implemented).</summary>
public sealed class AzureOnlyOcrWorkflow : IQueuedOcrWorkflow
{
    public string WorkflowKey => "AzureOnly";

    public Task ExecuteAsync(OcrJob job, CancellationToken ct) =>
        throw new NotImplementedException("AzureOnly workflow is not yet implemented.");
}
