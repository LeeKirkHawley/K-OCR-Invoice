namespace KOCRAsp.Models;

/// <summary>View-model for a single row in the file list panel.</summary>
public class FileListEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }
    public bool IsValidated { get; set; }
    public bool HasSuspectFields { get; set; }
    public bool IsSavedOrAccepted { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool ExceedsPageLimit { get; set; }

    public string ProcessedDotClass => IsProcessed ? "dot-validated" : "dot-unprocessed";
    public string ProcessedDotTitle => IsProcessed ? "Processed" : "Not processed";

    public string ValidationDotClass =>
        !IsProcessed          ? string.Empty
        : IsSavedOrAccepted   ? "dot-validated"
        : HasSuspectFields    ? "dot-suspect"
        : IsValidated         ? "dot-validated"
        : string.Empty;

    public string ValidationDotTitle =>
        IsSavedOrAccepted  ? "Validated"
        : HasSuspectFields ? "Suspect fields"
        : IsValidated      ? "Validated"
        : string.Empty;
}
