using K_OCR.Models;

namespace K_OCR.Services.Workflow;

/// <summary>
/// Workflow step that persists the completed pipeline context to the database.
/// </summary>
public class SaveContextStep : IWorkflowStep
{
    private readonly IFileService _fileService;

    public string Name => "Save Context";

    public SaveContextStep(IFileService fileService)
    {
        _fileService = fileService;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        await _fileService.SaveContextAsync(context.InputPath, context);
    }
}
