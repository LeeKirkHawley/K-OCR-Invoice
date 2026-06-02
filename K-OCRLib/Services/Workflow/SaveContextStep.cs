using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that persists the completed pipeline context to the database.
/// </summary>
public class SaveContextStep : WorkflowStepBase
{
    private readonly IFileService _fileService;

    public override string Name => "Save Context";

    public SaveContextStep(IFileService fileService)
    {
        _fileService = fileService;
    }

    public override async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        await _fileService.SaveContextAsync(context.InputPath, context);
    }
}
