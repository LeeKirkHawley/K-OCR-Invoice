using K_OCR.Models;

namespace K_OCR.Services;

public interface IInvoiceProcessingService
{
    /// <summary>
    /// Process a file (image or PDF) and return OCR results
    /// </summary>
    /// <param name="filePath">Path to the file to process</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <returns>Processing result with context and JSON</returns>
    Task<ProcessingResult> ProcessFileAsync(string filePath, bool useCache = true);
    
    /// <summary>
    /// Process multiple files in batch with progress reporting
    /// </summary>
    /// <param name="filePaths">Paths to files to process</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Dictionary of file paths to processing results</returns>
    Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        bool useCache = true,
        IProgress<(int completed, int total, string currentFile)>? progress = null);
    
    /// <summary>
    /// Save a validated invoice back to its JSON file
    /// </summary>
    /// <param name="originalFilePath">Original image/PDF file path</param>
    /// <param name="invoice">Validated invoice data</param>
    Task SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice);
    
    /// <summary>
    /// Load cached invoice from JSON file
    /// </summary>
    /// <param name="filePath">Path to the image/PDF file (not the JSON)</param>
    /// <returns>Cached invoice or null if not found</returns>
    Task<InvoiceDto?> LoadCachedInvoiceAsync(string filePath);
    
    /// <summary>
    /// Check if a file has cached processing results
    /// </summary>
    /// <param name="filePath">Path to the image/PDF file</param>
    /// <returns>True if cached results exist</returns>
    bool HasCachedResults(string filePath);
}
