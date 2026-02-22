using K_OCR.Models;
using K_OCR.PipelineService;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace K_OCR.Services;

public class InvoiceProcessingService : IInvoiceProcessingService
{
    private readonly IFileService _fileService;
    private readonly IInvoiceService _invoiceService;
    private readonly ITesseractValidationService _tesseractValidation;
    private readonly IInvoiceValidationService _invoiceValidation;
    private readonly ILineItemValidationService _lineItemValidation;
    private readonly IConfidenceValidationService _confidenceValidation;
    private readonly IConfiguration _configuration;
    private readonly string _defaultPipelineConfigPath;
    
    public InvoiceProcessingService(
        IFileService fileService,
        IInvoiceService invoiceService,
        ITesseractValidationService tesseractValidation,
        IInvoiceValidationService invoiceValidation,
        ILineItemValidationService lineItemValidation,
        IConfidenceValidationService confidenceValidation,
        IConfiguration configuration)
    {
        _fileService = fileService;
        _invoiceService = invoiceService;
        _tesseractValidation = tesseractValidation;
        _invoiceValidation = invoiceValidation;
        _lineItemValidation = lineItemValidation;
        _confidenceValidation = confidenceValidation;
        _configuration = configuration;
        _defaultPipelineConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "PipelineService", 
            "DefaultPipeline.json");
    }
    
    public bool HasCachedResults(string filePath)
    {
        return _fileService.HasCachedJson(filePath);
    }
    
    public async Task<ProcessingResult> ProcessFileAsync(string filePath, bool useCache = true, string? artifactsDirectory = null)
    {
        var result = new ProcessingResult();
        
        try
        {
            // Try to load from cache if enabled.
            // A cache entry is only usable if it already contains Tesseract text;
            // older entries (created before Tesseract validation was added) will
            // fall through so the full reprocessing path runs and the cache is updated.
            if (useCache)
            {
                var cachedContext = await _fileService.LoadCachedContextAsync(filePath, artifactsDirectory);
                if (cachedContext != null && !string.IsNullOrWhiteSpace(cachedContext.TesseractOcrText))
                {
                    // Re-run validation in memory — cheap, and guarantees the flags
                    // always reflect the current validation logic rather than stale cache.
                    RunValidation(cachedContext);
                    result.Context = cachedContext;
                    result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(cachedContext, Newtonsoft.Json.Formatting.Indented);
                    result.WasCached = true;
                    return result;
                }
                // Cache absent or predates Tesseract validation — fall through to full reprocessing.
            }
            
            // No usable cache — run pipeline
            var config = PipelineConfigLoader.Load(_defaultPipelineConfigPath);
            var executor = new PipelineExecutor(_invoiceService);
            var context = new PipelineContext
            {
                InputPath = filePath
            };

            // Run Azure OCR pipeline and local Tesseract validation concurrently.
            var azureTask = executor.RunAsync(config, context);
            var tesseractTask = _tesseractValidation.ExtractTextAsync(filePath, artifactsDirectory);

            // Await both tasks. A Tesseract failure must NOT abort the Azure result —
            // we capture any Tesseract exception and continue with an empty string so
            // validation can still flag all fields as unconfirmed.
            await Task.WhenAll(
                azureTask,
                tesseractTask.ContinueWith(_ => { }, TaskContinuationOptions.None));

            var processedContext = azureTask.Result;

            if (tesseractTask.IsCompletedSuccessfully)
            {
                processedContext.TesseractOcrText = tesseractTask.Result;
            }
            else
            {
                // Leave TesseractOcrText = null so RunValidation can distinguish
                // "task threw an exception" (infrastructure failure — skip validation)
                // from "ran successfully but produced no text" (empty string — flag fields).
                Console.WriteLine($"[TesseractValidation] Failed for {Path.GetFileName(filePath)}: {tesseractTask.Exception?.GetBaseException().Message}");
            }

            // Cross-validate every Azure-extracted field against the Tesseract text.
            RunValidation(processedContext);

            // Save to cache
            await _fileService.SaveContextAsync(filePath, processedContext, artifactsDirectory);
            
            result.Context = processedContext;
            result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(processedContext, Newtonsoft.Json.Formatting.Indented);
            result.WasCached = false;
            
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }
    }
    
    public async Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        bool useCache = true,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        string? artifactsDirectory = null)
    {
        var results = new Dictionary<string, ProcessingResult>();
        var filePathsList = filePaths.ToList();
        var total = filePathsList.Count;
        var completed = 0;
        
        // Use semaphore for concurrency control (reuse from InvoiceService)
        var tasks = filePathsList.Select(async filePath =>
        {
            try
            {
                progress?.Report((completed, total, Path.GetFileName(filePath)));
                
                var result = await ProcessFileAsync(filePath, useCache, artifactsDirectory);
                
                lock (results)
                {
                    results[filePath] = result;
                }
                
                Interlocked.Increment(ref completed);
                progress?.Report((completed, total, Path.GetFileName(filePath)));
            }
            catch (Exception ex)
            {
                lock (results)
                {
                    results[filePath] = new ProcessingResult { Error = ex };
                }
            }
        });
        
        await Task.WhenAll(tasks);
        return results;
    }
    
    public async Task SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice, string? artifactsDirectory = null)
    {
        // Only update ValidatedOcrText — OcrText always stays as the original OCR output.
        var invoices = new List<InvoiceDto> { invoice };
        await _fileService.SaveValidatedLayoutAsync(originalFilePath, invoices);
    }
    
    public async Task<InvoiceDto?> LoadCachedInvoiceAsync(string filePath, string? artifactsDirectory = null)
    {
        var context = await _fileService.LoadCachedContextAsync(filePath, artifactsDirectory);
        
        if (context?.Layout == null)
            return null;
        
        // Extract invoice from Layout property
        if (context.Layout is JArray layoutArray && layoutArray.Count > 0)
        {
            try
            {
                return layoutArray[0].ToObject<InvoiceDto>();
            }
            catch
            {
                return null;
            }
        }
        else if (context.Layout is List<InvoiceDto> invoiceList && invoiceList.Count > 0)
        {
            return invoiceList[0];
        }
        
        return null;
    }

    /// <summary>
    /// Runs cross-validation of all Azure-extracted invoice fields against the
    /// Tesseract text stored in <paramref name="context"/>.
    /// Handles both freshly-processed and cache-reloaded contexts.
    /// When the Layout is a <see cref="JArray"/> (Newtonsoft deserialised from cache),
    /// it is converted to <see cref="List{InvoiceDto}"/> before validation so the
    /// same code path is exercised regardless of how the context was loaded.
    /// </summary>
    private void RunValidation(PipelineContext context)
    {
        // null  = Tesseract task threw an exception (infrastructure failure)
        //         → skip Tesseract validation; leave TesseractConfirmed empty
        // ""    = Tesseract ran but extracted no text
        //         → ValidateAgainstTesseract will call FlagAllExtracted
        // other = normal text; validate field by field
        var tesseractText = context.TesseractOcrText; // intentionally NOT coalesced to ""

        List<InvoiceDto>? invoices = null;

        if (context.Layout is List<InvoiceDto> list)
        {
            invoices = list;
        }
        else if (context.Layout is JArray jArray)
        {
            try { invoices = jArray.ToObject<List<InvoiceDto>>(); } catch { }
        }

        if (invoices == null) return;

        foreach (var invoice in invoices)
        {
            // Only run Tesseract validation when text is available.
            // null  = task failed (infrastructure error) → skip so fields are not incorrectly flagged
            // ""    = task succeeded but found no text  → ValidateAgainstTesseract flags extracted fields
            if (tesseractText != null)
                _invoiceValidation.ValidateAgainstTesseract(invoice, tesseractText);
            
            // Run mathematical validation
            _lineItemValidation.ValidateInvoiceMath(invoice);

            // Run Azure confidence validation
            var minConfidence = _configuration.GetValue<double>("MinConfidenceThreshold", 0.8);
            _confidenceValidation.ValidateConfidence(invoice, minConfidence);
        }
    }
}
