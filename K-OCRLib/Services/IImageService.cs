using System.Runtime.Versioning;

namespace K_OCR.Services;

/// <summary>
/// Service for image conversion operations.
/// </summary>
public interface IImageService
{
    /// <summary>
    /// Converts a PDF file to PNG image(s).
    /// For single-page PDFs, creates one PNG with the same base name.
    /// For multi-page PDFs, creates multiple PNGs with page numbers (e.g., document_page1.png).
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file to convert.</param>
    /// <param name="artifactsDirectory">Optional directory to save PNG files. If null, saves in same directory as PDF.</param>
    /// <param name="maxDimension">Maximum width/height for the output image (default 1920).</param>
    /// <returns>
    /// Path to the first converted PNG file, or null if conversion failed.
    /// For multi-page PDFs, returns the path to the first page.
    /// </returns>
    /// <exception cref="FileNotFoundException">The PDF file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid PDF.</exception>
    /// <exception cref="InvalidOperationException">PDF conversion failed.</exception>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    Task<string?> ConvertPdfToPngAsync(string pdfPath, string? artifactsDirectory = null, int maxDimension = 1920);

    /// <summary>
    /// Gets all PNG paths that would be generated for a multi-page PDF.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="artifactsDirectory">Optional directory to save PNG files. If null, saves in same directory as PDF.</param>
    /// <param name="maxDimension">Maximum width/height for the output image (default 1920).</param>
    /// <returns>List of paths to all converted PNG files.</returns>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    Task<List<string>> ConvertPdfToAllPngsAsync(string pdfPath, string? artifactsDirectory = null, int maxDimension = 1920);
    
    /// <summary>
    /// Combines multiple image files vertically into a single composite image.
    /// </summary>
    /// <param name="imagePaths">List of image file paths to combine (in order from top to bottom).</param>
    /// <param name="outputPath">Path where the combined image will be saved.</param>
    /// <returns>Tuple of (combined image path, list of individual page heights in pixels).</returns>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    Task<(string filePath, List<int> pageHeights)> CombineImagesVerticallyAsync(List<string> imagePaths, string outputPath);
}
