namespace K_OCR.Models;

/// <summary>
/// View-model for a single row in the file list panel.
/// </summary>
public class FileListEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName  { get; set; } = string.Empty;

    /// <summary>File exists in the database (has been OCR-processed at least once).</summary>
    public bool IsProcessed      { get; set; }

    /// <summary>OCR ran AND all Tesseract cross-validation checks passed.</summary>
    public bool IsValidated      { get; set; }

    /// <summary>OCR ran AND at least one Tesseract cross-validation check failed.</summary>
    public bool HasSuspectFields { get; set; }

    /// <summary>CSS class applied to the status dot.</summary>
    public string DotClass =>
        HasSuspectFields ? "dot-suspect"
        : IsValidated    ? "dot-validated"
        : IsProcessed    ? "dot-processed"
        : "dot-unprocessed";

    /// <summary>Human-readable status for the tooltip.</summary>
    public string StatusLabel =>
        HasSuspectFields ? "Suspect fields"
        : IsValidated    ? "Validated"
        : IsProcessed    ? "Processed"
        : "Not processed";
}
