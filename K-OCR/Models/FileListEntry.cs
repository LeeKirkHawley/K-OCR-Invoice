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

    /// <summary>Invoice has been saved (user edits) or validation has been accepted.</summary>
    public bool IsSavedOrAccepted { get; set; }

    // ── Left dot: has this file been OCR-processed? ───────────────────────

    /// <summary>CSS class for the left (processed) status dot.</summary>
    public string ProcessedDotClass => IsProcessed ? "dot-validated" : "dot-unprocessed";

    /// <summary>Tooltip for the processed dot.</summary>
    public string ProcessedDotTitle => IsProcessed ? "Processed" : "Not processed";

    // ── Right dot: validation quality ───────────────────────────────────

    /// <summary>
    /// CSS class for the right (validation) dot.
    /// Empty string = not rendered (file not yet processed or no validation data).
    /// </summary>
    public string ValidationDotClass =>
        !IsProcessed          ? string.Empty
        : IsSavedOrAccepted   ? "dot-validated"
        : HasSuspectFields    ? "dot-suspect"
        : IsValidated         ? "dot-validated"
        : string.Empty;

    /// <summary>Tooltip for the validation dot.</summary>
    public string ValidationDotTitle =>
        IsSavedOrAccepted  ? "Validated"
        : HasSuspectFields ? "Suspect fields"
        : IsValidated      ? "Validated"
        : string.Empty;
}
