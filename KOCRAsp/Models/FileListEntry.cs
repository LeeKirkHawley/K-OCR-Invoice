namespace KOCRAsp.Models;

/// <summary>View-model for a single row in the file list panel.</summary>
public class FileListEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }
    public bool HasOcrResult { get; set; }
    public bool IsValidated { get; set; }
    public bool HasSuspectFields { get; set; }
    public bool IsSavedOrAccepted { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool ExceedsPageLimit { get; set; }

    public string ProcessedDotClass => "dot-validated";
    public string ProcessedDotTitle => "Uploaded";

    public string ValidationDotClass =>
        !HasOcrResult        ? string.Empty
        : IsSavedOrAccepted   ? "dot-validated"
        : "dot-suspect";

    public string ValidationDotTitle =>
        !HasOcrResult      ? string.Empty
        : IsSavedOrAccepted ? "Validated"
        : "OCR complete (validation pending)";
}
