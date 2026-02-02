using K_OCR.PipelineService;

namespace K_OCR.Models;

public class ProcessingResult
{
    /// <summary>
    /// The pipeline context containing all OCR results
    /// </summary>
    public PipelineContext? Context { get; set; }
    
    /// <summary>
    /// JSON representation of the context
    /// </summary>
    public string Json { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates whether the result was loaded from cache
    /// </summary>
    public bool WasCached { get; set; }
    
    /// <summary>
    /// Error that occurred during processing, if any
    /// </summary>
    public Exception? Error { get; set; }
    
    /// <summary>
    /// Indicates whether processing was successful
    /// </summary>
    public bool IsSuccess => Error == null && Context != null;
}
