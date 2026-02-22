using K_OCR.PipelineService;

namespace K_OCR.Services
{
    public interface IFileService
    {
        /// <summary>
        /// Load cached pipeline context from database
        /// </summary>
        Task<PipelineContext?> LoadCachedContextAsync(string imagePath, string? artifactsDirectory = null);
        
        /// <summary>
        /// Save pipeline context to database
        /// </summary>
        Task SaveContextAsync(string imagePath, PipelineContext context, string? artifactsDirectory = null);

        /// <summary>
        /// Save only the validated Layout (user edits) to ValidatedOcrText without
        /// touching OcrText, which always remains the original OCR output.
        /// </summary>
        Task SaveValidatedLayoutAsync(string imagePath, List<K_OCR.Models.InvoiceDto> invoices);
        
        /// <summary>
        /// Check if cached JSON exists for an image
        /// </summary>
        bool HasCachedJson(string imagePath);
        
        /// <summary>
        /// Load files from a directory, optionally filtered by extensions.
        /// Uses lazy enumeration for efficient handling of large directories.
        /// </summary>
        /// <param name="directory">The directory to search</param>
        /// <param name="extensions">Optional file extensions to filter (e.g., ".png", ".jpg"). If null, returns all files.</param>
        /// <returns>Lazy enumerable of file paths</returns>
        IEnumerable<string> LoadFiles(string directory, string[]? extensions = null);
        /// <summary>
        /// List directories and files for a given path
        /// </summary>
        IEnumerable<K_OCR.Models.DirectoryEntry> ListDirectory(string? path = null);
    }
}
