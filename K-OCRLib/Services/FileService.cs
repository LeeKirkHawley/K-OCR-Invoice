using System.IO;
using K_OCR.Models;
using K_OCR.PipelineService;
using System.IO;
using K_OCR.PipelineService;


namespace K_OCR.Services
{
    public class FileService : IFileService
    {
        private readonly DatabaseService _databaseService;

        public FileService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }
        public async Task<PipelineContext?> LoadCachedContextAsync(string imagePath, string? artifactsDirectory = null)
        {
            var ocrFile = await _databaseService.GetOCRFileByPathAsync(imagePath);
            if (ocrFile != null)
            {
                PipelineContext? context = null;

                // Load the main context from OcrText
                if (!string.IsNullOrEmpty(ocrFile.OcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(ocrFile.OcrText);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Database Cache] Failed to deserialize OcrText for {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }

                // If we have a context and ValidatedOcrText exists, replace the Layout with validated data
                if (context != null && !string.IsNullOrEmpty(ocrFile.ValidatedOcrText))
                {
                    try
                    {
                        // ValidatedOcrText contains the validated invoice array
                        var validatedInvoices = Newtonsoft.Json.JsonConvert.DeserializeObject<List<K_OCR.Models.InvoiceDto>>(ocrFile.ValidatedOcrText);
                        if (validatedInvoices != null)
                        {
                            context.Layout = validatedInvoices;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Database Cache] Failed to deserialize ValidatedOcrText for {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }

                // Restore TesseractOcrText from dedicated DB column when loading a context
                // written before the column existed (backward compatibility).
                if (context != null
                    && string.IsNullOrWhiteSpace(context.TesseractOcrText)
                    && !string.IsNullOrWhiteSpace(ocrFile.TesseractOcrText))
                {
                    context.TesseractOcrText = ocrFile.TesseractOcrText;
                }

                return context;
            }

            return null;
        }
        
        public async Task SaveContextAsync(string imagePath, PipelineContext context, string? artifactsDirectory = null)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
            var isFullyProcessed = context.Layout != null && !string.IsNullOrWhiteSpace(context.TesseractOcrText);

            // Serialize validated invoice with TesseractConfirmed for database storage
            string? validatedJson = null;
            if (context.Layout != null)
            {
                try
                {
                    validatedJson = Newtonsoft.Json.JsonConvert.SerializeObject(context.Layout, Newtonsoft.Json.Formatting.Indented);
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[SaveContext] Error serializing validated invoice: {ex.Message}");
                }
            }

            // Upsert: load existing row so we never create duplicate entries for the same file.
            var existing = await _databaseService.GetOCRFileByPathAsync(imagePath);

            if (existing != null)
            {
                existing.OcrText = json;
                existing.TesseractOcrText = context.TesseractOcrText;
                existing.ValidatedOcrText = validatedJson;
                existing.IsFullyProcessed = isFullyProcessed;
                await _databaseService.SaveOCRFileAsync(existing);
                System.Console.WriteLine($"[Database Cache] Updated OCR data for: {Path.GetFileName(imagePath)}");
            }
            else
            {
                var ocrFile = new OCRFile
                {
                    FilePath = imagePath,
                    OcrText = json,
                    TesseractOcrText = context.TesseractOcrText,
                    ValidatedOcrText = validatedJson,
                    IsFullyProcessed = isFullyProcessed
                };
                await _databaseService.SaveOCRFileAsync(ocrFile);
                System.Console.WriteLine($"[Database Cache] Saved OCR data for: {Path.GetFileName(imagePath)}");
            }
        }
        
        public async Task SaveValidatedLayoutAsync(string imagePath, List<K_OCR.Models.InvoiceDto> invoices)
        {
            var validatedJson = Newtonsoft.Json.JsonConvert.SerializeObject(invoices, Newtonsoft.Json.Formatting.Indented);

            var existing = await _databaseService.GetOCRFileByPathAsync(imagePath);
            if (existing != null)
            {
                existing.ValidatedOcrText = validatedJson;
                await _databaseService.SaveOCRFileAsync(existing);
            }
            else
            {
                // No OCR row yet — create a minimal one (edge case: validate before OCR)
                var ocrFile = new OCRFile
                {
                    FilePath         = imagePath,
                    OcrText          = string.Empty,
                    ValidatedOcrText = validatedJson,
                    IsFullyProcessed = false
                };
                await _databaseService.SaveOCRFileAsync(ocrFile);
            }
        }

        public bool HasCachedJson(string imagePath)
        {
            var ocrFile = _databaseService.GetOCRFileByPathAsync(imagePath).GetAwaiter().GetResult();
            return ocrFile != null && (!string.IsNullOrEmpty(ocrFile.OcrText) || !string.IsNullOrEmpty(ocrFile.ValidatedOcrText));
        }
        
        public IEnumerable<string> LoadFiles(string directory, string[]? extensions = null)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            } 

            if (extensions == null)
            {
                // Return all files when no extensions specified
                return Directory.EnumerateFiles(directory).OrderBy(f => f);
            }

            // HashSet for O(1) lookup instead of array O(n) - important for large directories
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            
            // Single directory traversal, lazy streaming, efficient filtering
            return Directory.EnumerateFiles(directory)
                .Where(f => extensionSet.Contains(Path.GetExtension(f)))
                .OrderBy(f => f);
        }

    public IEnumerable<DirectoryEntry> ListDirectory(string? path = null)
    {
        var entries = new List<DirectoryEntry>();
        var dir = string.IsNullOrEmpty(path) ? Directory.GetDirectoryRoot(Directory.GetCurrentDirectory()) : path;
        if (!Directory.Exists(dir))
            return entries;

        // Add directories
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            entries.Add(new DirectoryEntry
            {
                Name = Path.GetFileName(d),
                Path = d,
                IsDirectory = true
            });
        }
        // Add files
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            entries.Add(new DirectoryEntry
            {
                Name = Path.GetFileName(f),
                Path = f,
                IsDirectory = false
            });
        }
        return entries;
    }

    }
}
