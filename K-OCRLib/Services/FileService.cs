using System.IO;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace K_OCRLib.Services
{
    public class FileService : IFileService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<FileService> _logger;

        public FileService(DatabaseService databaseService, ILogger<FileService> logger)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logger = logger;
        }
        public async Task<PipelineContext?> LoadContextAsync(string imagePath)
        {
            var invoice = await _databaseService.GetInvoiceByFilePathAsync(imagePath);
            if (invoice != null)
            {
                PipelineContext? context = null;

                if (!string.IsNullOrEmpty(invoice.OcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(invoice.OcrText);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Database] Failed to deserialize OcrText for {FileName}.", Path.GetFileName(imagePath));
                    }
                }

                if (context != null && !string.IsNullOrEmpty(invoice.ValidatedOcrText))
                {
                    try
                    {
                        var validatedInvoices = Newtonsoft.Json.JsonConvert.DeserializeObject<List<InvoiceDto>>(invoice.ValidatedOcrText);
                        if (validatedInvoices != null)
                            context.Layout = validatedInvoices;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Database] Failed to deserialize ValidatedOcrText for {FileName}.", Path.GetFileName(imagePath));
                    }
                }

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
        
        public async Task SaveContextAsync(string imagePath, PipelineContext context)
        {
            string json = "";
            try
            {
                json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serialize pipeline context for {FileName}.", Path.GetFileName(imagePath));
            }

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
                    _logger.LogError(ex, "[SaveContext] Error serializing validated invoice for {FileName}.", Path.GetFileName(imagePath));
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
                _logger.LogDebug("[Database] Updated OCR data for: {FileName}.", Path.GetFileName(imagePath));
            }
            else
            {
                _logger.LogWarning("[Database] No invoice record found for {FileName} — skipping save.", Path.GetFileName(imagePath));
            }
        }
        
        public async Task SaveValidatedLayoutAsync(string imagePath, InvoiceDto invoice)
        {
            // Serialize as a single-element list to remain consistent with LoadCachedContextAsync
            // which deserialises ValidatedOcrText as List<InvoiceDto>.
            string validatedJson = ""; 


            try
            {
                validatedJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new List<InvoiceDto> { invoice }, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize validated json for {FileName}.", Path.GetFileName(imagePath));
            }

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
                existing.Notes                = invoice.Notes;
                existing.VendorCountry        = invoice.VendorCountry;
                existing.CurrencyCode         = invoice.CurrencyCode;

                // Preserve the caller's validation state — AcceptValidation sets
                // IsValidationAccepted = true on the DTO before calling here;
                // a plain Save leaves it unchanged.
                existing.ValidatedOcrText     = validatedJson;
                existing.IsValidationAccepted = invoice.IsValidationAccepted;
                existing.ProcessedAtUtc       = DateTime.UtcNow;

                await _databaseService.SaveInvoiceAsync(existing);
            }
            else
            {
                _logger.LogWarning("[Database] No invoice record found for {FileName} — skipping validated layout save.", Path.GetFileName(imagePath));
            }
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
