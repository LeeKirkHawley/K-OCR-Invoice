using System.IO;
using K_OCR.Models;
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
            var invoice = await _databaseService.GetInvoiceByFilePathAsync(imagePath);
            if (invoice != null)
            {
                PipelineContext? context = null;

                // Load the main context from OcrText
                if (!string.IsNullOrEmpty(invoice.OcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(invoice.OcrText);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Database Cache] Failed to deserialize OcrText for {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }

                // If we have a context and ValidatedOcrText exists, replace the Layout with validated data
                if (context != null && !string.IsNullOrEmpty(invoice.ValidatedOcrText))
                {
                    try
                    {
                        // ValidatedOcrText contains the validated invoice array
                        var validatedInvoices = Newtonsoft.Json.JsonConvert.DeserializeObject<List<K_OCR.Models.InvoiceDto>>(invoice.ValidatedOcrText);
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
                    && !string.IsNullOrWhiteSpace(invoice.TesseractOcrText))
                {
                    context.TesseractOcrText = invoice.TesseractOcrText;
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

            // Upsert: load existing invoice row and update OCR fields
            var existing = await _databaseService.GetInvoiceByFilePathAsync(imagePath);

            if (existing != null)
            {
                existing.OcrText = json;
                existing.TesseractOcrText = context.TesseractOcrText;
                existing.ValidatedOcrText = validatedJson;
                existing.IsFullyProcessed = isFullyProcessed;
                existing.ProcessedAtUtc = isFullyProcessed ? DateTime.UtcNow : existing.ProcessedAtUtc;
                await _databaseService.SaveInvoiceAsync(existing);
                System.Console.WriteLine($"[Database Cache] Updated OCR data for: {Path.GetFileName(imagePath)}");
            }
            else
            {
                // Cannot create invoice without BatchId; log and skip
                System.Console.WriteLine($"[Database Cache] No invoice record found for: {Path.GetFileName(imagePath)} — skipping save");
            }
        }
        
        public async Task SaveValidatedLayoutAsync(string imagePath, K_OCR.Models.InvoiceDto invoice)
        {
            // Serialize as a single-element list to remain consistent with LoadCachedContextAsync
            // which deserialises ValidatedOcrText as List<InvoiceDto>.
            var validatedJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                new List<K_OCR.Models.InvoiceDto> { invoice }, Newtonsoft.Json.Formatting.Indented);

            var existing = await _databaseService.GetInvoiceByFilePathAsync(imagePath);
            if (existing != null)
            {
                // Update all scalar invoice fields from the DTO
                existing.VendorName           = invoice.VendorName;
                existing.CustomerName         = invoice.CustomerName;
                existing.InvoiceId            = invoice.InvoiceId;
                existing.PurchaseOrder        = invoice.PurchaseOrder;
                existing.Subtotal             = invoice.Subtotal;
                existing.TotalTax             = invoice.TotalTax;
                existing.Shipping             = invoice.Shipping;
                existing.Total                = invoice.Total;
                existing.InvoiceDate          = DateTime.TryParse(invoice.InvoiceDate, out var invDate) ? invDate : null;
                existing.DueDate              = DateTime.TryParse(invoice.DueDate,     out var dueDate) ? dueDate : null;

                // Mark the invoice as human-validated and record when
                existing.ValidatedOcrText     = validatedJson;
                existing.IsValidationAccepted = true;
                existing.ProcessedAtUtc       = DateTime.UtcNow;

                await _databaseService.SaveInvoiceAsync(existing);
            }
            else
            {
                System.Console.WriteLine($"[Database Cache] No invoice record found for: {Path.GetFileName(imagePath)} — skipping validated layout save");
            }
        }

        public bool HasCachedJson(string imagePath)
        {
            var invoice = _databaseService.GetInvoiceByFilePathAsync(imagePath).GetAwaiter().GetResult();
            return invoice != null && (!string.IsNullOrEmpty(invoice.OcrText) || !string.IsNullOrEmpty(invoice.ValidatedOcrText));
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
