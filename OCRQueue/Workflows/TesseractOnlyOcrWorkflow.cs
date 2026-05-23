using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace OCRQueue.Workflows;

/// <summary>Stub: Tesseract-only OCR workflow (not yet implemented).</summary>
public sealed class TesseractOnlyOcrWorkflow : IQueuedOcrWorkflow
{
    public string WorkflowKey => "TesseractOnly";

    public Task ExecuteAsync(OcrJob job, CancellationToken ct) =>
        throw new NotImplementedException("TesseractOnly workflow is not yet implemented.");
}
