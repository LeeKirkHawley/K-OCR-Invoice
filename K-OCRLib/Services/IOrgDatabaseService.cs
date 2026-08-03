using K_OCRLib.Models;

namespace K_OCRLib.Services
{
    public interface IOrgDatabaseService
    {
        Task ClearAllDataAsync();
        Task<bool> DeleteInvoiceAsync(int id);
        Task<List<Invoice>> GetAllInvoicesAsync();
        Task<Invoice?> GetInvoiceByFilePathAsync(string filePath);
        Task<Invoice?> GetInvoiceByIdAsync(int id);
        Task<List<Invoice>> GetInvoicesByBatchAsync(int batchId);
        Task<bool> IsDatabaseAvailableAsync();
        Task<Invoice> SaveInvoiceAsync(Invoice invoice);
        Task<FieldEditDTO> SaveInvoiceEdits(FieldEditDTO fieldEditDTO);

        /// <summary>
        /// Load pipeline context from database
        /// </summary>
        Task<InvoiceDto?> LoadContextAsync(string imagePath);

        /// <summary>
        /// Save pipeline context to database
        /// </summary>
        Task SaveContextAsync(string imagePath, PipelineContext context);

        /// <summary>
        /// Save the validated Invoice DTO back to the Invoice row: updates all scalar
        /// field values, sets IsValidationAccepted = true, ProcessedAtUtc, and ValidatedOcrText.
        /// OcrText (original OCR output) is never changed by this method.
        /// </summary>
        Task SaveValidatedLayoutAsync(string imagePath, InvoiceDto invoice);

        /// <summary>
        /// Load files from a directory, optionally filtered by extensions.
        /// Uses lazy enumeration for efficient handling of large directories.
        /// </summary>
        /// <param name="directory">The directory to search</param>
        /// <param name="extensions">Optional file extensions to filter (e.g., ".png", ".jpg"). If null, returns all files.</param>
        /// <returns>Lazy enumerable of file paths</returns>

    }
}