namespace K_OCR.Services;

/// <summary>
/// Performs local Tesseract OCR on an image as a secondary validation pass
/// alongside the primary Azure Document Intelligence OCR.
/// </summary>
public interface ITesseractValidationService
{
    /// <summary>
    /// Extracts plain text from the given image file using the local Tesseract engine.
    /// </summary>
    /// <param name="filePath">
    /// Absolute path to the source file. Images (PNG, JPG, TIFF, BMP) are passed
    /// directly to Tesseract. PDF files are first converted to per-page PNGs using
    /// <see cref="IImageService"/> and then OCR'd page by page.
    /// </param>
    /// <param name="artifactsDirectory">
    /// Directory used to store (or reuse) the per-page PNG images generated when
    /// the source file is a PDF. Ignored for image files.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// The raw text extracted by Tesseract (all pages concatenated for PDFs), or
    /// an empty string if the file cannot be processed.
    /// </returns>
    Task<string> ExtractTextAsync(
        string filePath,
        string? artifactsDirectory = null,
        CancellationToken cancellationToken = default);
}
