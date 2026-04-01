using K_OCR.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<InvoiceProcessingService> _logger;
    
    public InvoiceProcessingService(
        IFileService fileService,
        IInvoiceService invoiceService,
        ITesseractValidationService tesseractValidation,
        IInvoiceValidationService invoiceValidation,
        ILineItemValidationService lineItemValidation,
        IConfidenceValidationService confidenceValidation,
        IConfiguration configuration,
        ILogger<InvoiceProcessingService> logger)
    {
        _fileService = fileService;
        _invoiceService = invoiceService;
        _tesseractValidation = tesseractValidation;
        _invoiceValidation = invoiceValidation;
        _lineItemValidation = lineItemValidation;
        _confidenceValidation = confidenceValidation;
        _configuration = configuration;
        _logger = logger;
    }
    
    public bool HasCachedResults(string filePath)
    {
        return _fileService.HasCachedJson(filePath);
    }
    
    public async Task<ProcessingResult> ProcessFileAsync(string filePath, bool useCache = true, string? artifactsDirectory = null, double? minConfidenceThreshold = null)
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
                    RunValidation(cachedContext, minConfidenceThreshold);
                    result.Context = cachedContext;
                    result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(cachedContext, Newtonsoft.Json.Formatting.Indented);
                    result.WasCached = true;
                    return result;
                }
                // Cache absent or predates Tesseract validation — fall through to full reprocessing.
            }
            
            // No usable cache — run Azure OCR and local Tesseract validation concurrently.
            async Task<PipelineContext> RunAzureAsync()
            {
                var ctx = new PipelineContext { InputPath = filePath };
                ctx.Layout = await _invoiceService.RunAzureInvoiceParse(filePath);
                return ctx;
            }

            var azureTask = RunAzureAsync();
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
                _logger.LogError(tesseractTask.Exception?.GetBaseException(), "[TesseractValidation] Failed for {FileName}.", Path.GetFileName(filePath));
            }

            // Cross-validate every Azure-extracted field against the Tesseract text.
            RunValidation(processedContext, minConfidenceThreshold);

            // Save to cache
            await _fileService.SaveContextAsync(filePath, processedContext, artifactsDirectory);
            
            result.Context = processedContext;
            result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(processedContext, Newtonsoft.Json.Formatting.Indented);
            result.WasCached = false;
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FilePath}.", filePath);
            result.Error = ex;
            return result;
        }
    }
    
    public async Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        bool useCache = true,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        string? artifactsDirectory = null,
        double? minConfidenceThreshold = null)
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
                
                var result = await ProcessFileAsync(filePath, useCache, artifactsDirectory, minConfidenceThreshold);
                
                lock (results)
                {
                    results[filePath] = result;
                }
                
                Interlocked.Increment(ref completed);
                progress?.Report((completed, total, Path.GetFileName(filePath)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file {FileName} in batch.", Path.GetFileName(filePath));
                lock (results)
                {
                    results[filePath] = new ProcessingResult { Error = ex };
                }
            }
        });
        
        await Task.WhenAll(tasks);
        return results;
    }
    
    public async Task SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice)
    {
        // Updates all scalar fields, IsValidationAccepted, ProcessedAtUtc, and ValidatedOcrText
        // on the Invoice row. OcrText (original OCR output) is never overwritten here.
        await _fileService.SaveValidatedLayoutAsync(originalFilePath, invoice);
    }
    
    public async Task<InvoiceDto?> LoadCachedInvoiceAsync(string filePath)
    {
        var context = await _fileService.LoadCachedContextAsync(filePath);
        return context?.Layout?.FirstOrDefault();
    }

    /// <summary>
    /// Runs cross-validation of all Azure-extracted invoice fields against the
    /// Tesseract text stored in <paramref name="context"/>.
    /// </summary>
    private void RunValidation(PipelineContext context, double? minConfidenceThreshold = null)
    {
        // null  = Tesseract task threw an exception (infrastructure failure)
        //         → skip Tesseract validation; leave TesseractConfirmed empty
        // ""    = Tesseract ran but extracted no text
        //         → ValidateAgainstTesseract will call FlagAllExtracted
        // other = normal text; validate field by field
        var tesseractText = context.TesseractOcrText; // intentionally NOT coalesced to ""

        if (context.Layout == null) return;

        foreach (var invoice in context.Layout)
        {
            if (tesseractText != null)
                _invoiceValidation.ValidateAgainstTesseract(invoice, tesseractText);
            
            _lineItemValidation.ValidateInvoiceMath(invoice);

            var minConfidence = minConfidenceThreshold ?? _configuration.GetValue<double>("MinConfidenceThreshold", 0.8);
            _confidenceValidation.ValidateConfidence(invoice, minConfidence);
        }
    }
}
