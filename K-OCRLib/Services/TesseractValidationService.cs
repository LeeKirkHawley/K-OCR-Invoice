using K_OCRLib.Services.Interfaces;
using Tesseract;

namespace K_OCRLib.Services;

/// <summary>
/// Runs local Tesseract OCR to produce a secondary text extraction for validation
/// against the primary Azure Document Intelligence result.
/// Supports both image files (PNG, JPG, TIFF, BMP) and PDF files.
/// For PDFs each page is first rendered to a PNG via <see cref="IImageService"/>
/// and then OCR'd individually; the page texts are concatenated in order.
/// </summary>
public class TesseractValidationService : ITesseractValidationService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".bmp"
    };

    private readonly IImageService _imageService;
    private readonly string _tessdataPath;
    private readonly string _language;

    /// <param name="imageService">Used to convert PDF pages to PNG images.</param>
    /// <param name="tessdataPath">
    /// Optional explicit path to the tessdata directory. When null the service
    /// probes well-known locations: the <c>TESSDATA_PREFIX</c> environment
    /// variable, a local <c>./tessdata</c> folder, then common system paths.
    /// </param>
    /// <param name="language">Tesseract language code (default: "eng").</param>
    public TesseractValidationService(
        IImageService imageService,
        string? tessdataPath = null,
        string language = "eng")
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _tessdataPath = tessdataPath ?? ResolveTessdataPath();
        _language = language;
    }

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        string filePath,
        string? artifactsDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(filePath);

        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            return await ExtractFromPdfAsync(filePath, artifactsDirectory, cancellationToken);

        if (ImageExtensions.Contains(ext))
            return await Task.Run(() => RunTesseract(filePath), cancellationToken);

        // Unsupported type — nothing to do
        return string.Empty;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts every page of <paramref name="pdfPath"/> to a PNG (reusing
    /// existing files on disk), then OCRs each page and concatenates the results.
    /// </summary>
    private async Task<string> ExtractFromPdfAsync(
        string pdfPath,
        string? artifactsDirectory,
        CancellationToken cancellationToken)
    {
        var pageImages = await _imageService.ConvertPdfToAllPngsAsync(
            pdfPath, artifactsDirectory);

        if (pageImages.Count == 0)
            return string.Empty;

        var pageTexts = new string[pageImages.Count];

        // OCR all pages in parallel on thread-pool threads
        await Parallel.ForEachAsync(
            pageImages.Select((path, index) => (path, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = cancellationToken
            },
            (item, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                pageTexts[item.index] = RunTesseract(item.path);
                return ValueTask.CompletedTask;
            });

        return string.Join("\n\n", pageTexts.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private string RunTesseract(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found for Tesseract OCR: {imagePath}", imagePath);

        using var engine = new TesseractEngine(_tessdataPath, _language, EngineMode.Default);
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText() ?? string.Empty;
    }

    /// <summary>
    /// Resolves the tessdata directory by probing in priority order:
    /// 1. <c>TESSDATA_PREFIX</c> environment variable
    /// 2. Local <c>./tessdata</c> relative to the executable
    /// 3. Common system install paths (Linux then Windows)
    /// </summary>
    private static string ResolveTessdataPath()
    {
        // 1. Environment variable
        var envPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
            return envPath;

        // 2. Local copy next to the executable (useful for self-contained deployments)
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        if (Directory.Exists(localPath))
            return localPath;

        // 3. Known system paths (Linux then Windows)
        string[] systemPaths =
        [
            "/usr/share/tesseract-ocr/5/tessdata",   // Ubuntu / Debian apt package
            "/usr/share/tesseract-ocr/4/tessdata",
            "/usr/share/tessdata",
            "/usr/local/share/tessdata",
            @"C:\Program Files\Tesseract-OCR\tessdata",
            @"C:\Program Files (x86)\Tesseract-OCR\tessdata",
        ];

        foreach (var path in systemPaths)
        {
            if (Directory.Exists(path))
                return path;
        }

        throw new InvalidOperationException(
            "Tesseract tessdata directory could not be found. " +
            "Install the tesseract-ocr package or set the TESSDATA_PREFIX " +
            "environment variable to the directory containing *.traineddata files.");
    }
}
