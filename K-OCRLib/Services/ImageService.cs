using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace K_OCR.Services;

/// <summary>
/// Service for image conversion operations using SkiaSharp and Docnet.Core.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public class ImageService : IImageService
{
    private readonly ILogger<ImageService> _logger;

    public ImageService(ILogger<ImageService> logger)
    {
        _logger = logger;
    }
    /// <inheritdoc />
    public async Task<string?> ConvertPdfToPngAsync(string pdfPath, string? artifactsDirectory = null, int maxDimension = 1920)
    {
        ValidatePdfFile(pdfPath);

        if (string.IsNullOrEmpty(artifactsDirectory))
        {
            throw new InvalidOperationException("Artifacts directory must be configured before converting PDFs. Please set the Project Artifacts directory in Settings.");
        }

        var directory = artifactsDirectory;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(pdfPath);

        // Ensure the output directory exists
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        int pageCount;
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(maxDimension, maxDimension));
            pageCount = docReader.GetPageCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImageService] Unable to open PDF for conversion: {PdfPath}.", Path.GetFileName(pdfPath));
            throw new InvalidOperationException($"Unable to read PDF: {ex.Message}", ex);
        }

        if (pageCount == 0)
            return null;

        // For single-page PDFs, create one PNG
        if (pageCount == 1)
        {
            var outputPath = Path.Combine(directory, $"{fileNameWithoutExt}.png");

            // Check if PNG already exists
            if (File.Exists(outputPath))
                return outputPath;

            await ConvertPageToPngAsync(pdfPath, 0, outputPath, maxDimension);
            return outputPath;
        }
        else
        {
            // For multi-page PDFs, create multiple PNGs with page numbers
            var outputPaths = new List<string>();

            for (int page = 0; page < pageCount; page++)
            {
                var outputPath = Path.Combine(directory, $"{fileNameWithoutExt}_page{page + 1}.png");

                // Skip if already exists
                if (!File.Exists(outputPath))
                {
                    try
                    {
                        await ConvertPageToPngAsync(pdfPath, page, outputPath, maxDimension);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ImageService] Failed to convert page {Page} of {PdfPath}.", page + 1, Path.GetFileName(pdfPath));
                        continue;
                    }
                }

                if (File.Exists(outputPath))
                    outputPaths.Add(outputPath);
            }

            // Return the first page
            return outputPaths.FirstOrDefault();
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> ConvertPdfToAllPngsAsync(string pdfPath, string? artifactsDirectory = null, int maxDimension = 1920)
    {
        ValidatePdfFile(pdfPath);

        if (string.IsNullOrEmpty(artifactsDirectory))
        {
            throw new InvalidOperationException("Artifacts directory must be configured before converting PDFs. Please set the Project Artifacts directory in Settings.");
        }

        var directory = artifactsDirectory;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(pdfPath);

        // Ensure the output directory exists
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var outputPaths = new List<string>();

        int pageCount;
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(maxDimension, maxDimension));
            pageCount = docReader.GetPageCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImageService] Unable to open PDF for conversion: {PdfPath}.", Path.GetFileName(pdfPath));
            throw new InvalidOperationException($"Unable to read PDF: {ex.Message}", ex);
        }

        if (pageCount == 0)
            return outputPaths;

        // For single-page PDFs, create one PNG
        if (pageCount == 1)
        {
            var outputPath = Path.Combine(directory, $"{fileNameWithoutExt}.png");

            if (!File.Exists(outputPath))
            {
                await ConvertPageToPngAsync(pdfPath, 0, outputPath, maxDimension);
            }

            if (File.Exists(outputPath))
                outputPaths.Add(outputPath);
        }
        else
        {
            // For multi-page PDFs, create multiple PNGs with page numbers
            for (int page = 0; page < pageCount; page++)
            {
                var outputPath = Path.Combine(directory, $"{fileNameWithoutExt}_page{page + 1}.png");

                if (!File.Exists(outputPath))
                {
                    try
                    {
                        await ConvertPageToPngAsync(pdfPath, page, outputPath, maxDimension);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ImageService] Failed to convert page {Page} of {PdfPath}.", page + 1, Path.GetFileName(pdfPath));
                        continue;
                    }
                }

                if (File.Exists(outputPath))
                    outputPaths.Add(outputPath);
            }
        }

        return outputPaths;
    }

    private static void ValidatePdfFile(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found.", pdfPath);

        // Check if it's actually a PDF by reading the header
        using var fs = File.OpenRead(pdfPath);
        var header = new byte[4];
        if (fs.Read(header, 0, 4) < 4 ||
            header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46) // %PDF
        {
            throw new InvalidDataException("The file is not a valid PDF.");
        }
    }

    private static Task ConvertPageToPngAsync(string pdfPath, int pageIndex, string outputPath, int maxDimension)
    {
        return Task.Run(() =>
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(maxDimension, maxDimension));
            using var pageReader = docReader.GetPageReader(pageIndex);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            // Create SkiaSharp bitmap from raw BGRA bytes
            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            Marshal.Copy(rawBytes, 0, bitmap.GetPixels(), rawBytes.Length);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);
        });
    }
    
    /// <inheritdoc />
    public async Task<(string filePath, List<int> pageHeights)> CombineImagesVerticallyAsync(List<string> imagePaths, string outputPath)
    {
        return await Task.Run(() =>
        {
            if (imagePaths == null || imagePaths.Count == 0)
                throw new ArgumentException("No images provided to combine");
            
            // Load all images and get total height
            var bitmaps = new List<SKBitmap>();
            var pageHeights = new List<int>();
            int maxWidth = 0;
            int totalHeight = 0;
            
            try
            {
                foreach (var path in imagePaths)
                {
                    var bitmap = SKBitmap.Decode(path);
                    if (bitmap != null)
                    {
                        bitmaps.Add(bitmap);
                        pageHeights.Add(bitmap.Height);
                        maxWidth = Math.Max(maxWidth, bitmap.Width);
                        totalHeight += bitmap.Height;
                    }
                }
                
                if (bitmaps.Count == 0)
                    throw new InvalidOperationException("No valid images found");
                
                // Create combined bitmap
                using var combined = new SKBitmap(maxWidth, totalHeight);
                using var canvas = new SKCanvas(combined);
                canvas.Clear(SKColors.White);
                
                // Draw each bitmap
                int currentY = 0;
                foreach (var bitmap in bitmaps)
                {
                    canvas.DrawBitmap(bitmap, 0, currentY);
                    currentY += bitmap.Height;
                }
                
                // Save combined image
                using var image = SKImage.FromBitmap(combined);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(outputPath);
                data.SaveTo(stream);
                
                return (outputPath, pageHeights);
            }
            finally
            {
                // Dispose all bitmaps
                foreach (var bitmap in bitmaps)
                {
                    bitmap.Dispose();
                }
            }
        });
    }
}
