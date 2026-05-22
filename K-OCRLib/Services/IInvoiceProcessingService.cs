using K_OCR.Models;

namespace K_OCR.Services;

public interface IInvoiceProcessingService
{
    /// <summary>
    /// Process a file (image or PDF) through Azure OCR and return results.
    /// Always calls Azure — cached results are never substituted.
    /// </summary>
    Task<ProcessingResult> ProcessFileAsync(string filePath, string? artifactsDirectory = null, double? minConfidenceThreshold = null);
    
    /// <summary>
    /// Process multiple files in batch with progress reporting.
    /// Always calls Azure for every file — cached results are never substituted.
    /// </summary>
    Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        string? artifactsDirectory = null,
        double? minConfidenceThreshold = null,
        Batch? batch = null,
        string? organizationName = null);
    
    /// <summary>
    /// Save a validated invoice to the database Invoice row.
    /// </summary>
    Task SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice);
    
    /// <summary>
    /// Load a previously processed invoice from the database.
    /// </summary>
    Task<InvoiceDto?> LoadInvoiceAsync(string filePath);
}
