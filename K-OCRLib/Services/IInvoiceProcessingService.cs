using K_OCR.Models;

namespace K_OCR.Services;

public interface IInvoiceProcessingService
{
    /// <summary>
    /// Process a file (image or PDF) and return OCR results
    /// </summary>
    /// <param name="filePath">Path to the file to process</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="artifactsDirectory">Directory where artifacts are stored (optional)</param>
    /// <returns>Processing result with context and JSON</returns>
    Task<ProcessingResult> ProcessFileAsync(string filePath, bool useCache = true, string? artifactsDirectory = null);
    
    /// <summary>
    /// Process multiple files in batch with progress reporting
    /// </summary>
    /// <param name="filePaths">Paths to files to process</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="artifactsDirectory">Directory where artifacts are stored (optional)</param>
    /// <returns>Dictionary of file paths to processing results</returns>
    Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        bool useCache = true,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        string? artifactsDirectory = null);
    
    /// <summary>
    /// Save a validated invoice to the database Invoice row.
    /// Updates all scalar field values, sets IsValidationAccepted = true, ProcessedAtUtc, and ValidatedOcrText.
    /// </summary>
    /// <param name="originalFilePath">Original image/PDF file path (used to look up the Invoice row)</param>
    /// <param name="invoice">Validated invoice data</param>
    Task SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice);
    
    /// <summary>
    /// Load cached invoice from the database Invoice row (reads ValidatedOcrText).
    /// </summary>
    /// <param name="filePath">Path to the image/PDF file (used to look up the Invoice row)</param>
    /// <returns>Cached invoice or null if not found</returns>
    Task<InvoiceDto?> LoadCachedInvoiceAsync(string filePath);
    
    /// <summary>
    /// Check if a file has cached processing results
    /// </summary>
    /// <param name="filePath">Path to the image/PDF file</param>
    /// <returns>True if cached results exist</returns>
    bool HasCachedResults(string filePath);
}
