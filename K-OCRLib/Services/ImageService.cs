using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace K_OCR.Services;

/// <summary>
/// Service for image conversion operations using SkiaSharp and Docnet.Core.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public class ImageService : IImageService
{
    /// <inheritdoc />
    public async Task<string?> ConvertPdfToPngAsync(string pdfPath, int maxDimension = 1920)
    {
        ValidatePdfFile(pdfPath);

        var directory = Path.GetDirectoryName(pdfPath)!;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(pdfPath);

        int pageCount;
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(maxDimension, maxDimension));
            pageCount = docReader.GetPageCount();
        }
        catch (Exception ex)
        {
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
                        // Log but continue with other pages
                        Console.WriteLine($"[ImageService] Failed to convert page {page + 1}: {ex.Message}");
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
    public async Task<List<string>> ConvertPdfToAllPngsAsync(string pdfPath, int maxDimension = 1920)
    {
        ValidatePdfFile(pdfPath);

        var directory = Path.GetDirectoryName(pdfPath)!;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(pdfPath);
        var outputPaths = new List<string>();

        int pageCount;
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(maxDimension, maxDimension));
            pageCount = docReader.GetPageCount();
        }
        catch (Exception ex)
        {
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
                        Console.WriteLine($"[ImageService] Failed to convert page {page + 1}: {ex.Message}");
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
}
