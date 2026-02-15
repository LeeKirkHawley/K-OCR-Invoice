using K_OCR.Models;
using K_OCR.PipelineService;
using Newtonsoft.Json.Linq;

namespace K_OCR.Services;

public class InvoiceProcessingService : IInvoiceProcessingService
{
    private readonly IFileService _fileService;
    private readonly IInvoiceService _invoiceService;
    private readonly string _defaultPipelineConfigPath;
    
    public InvoiceProcessingService(IFileService fileService, IInvoiceService invoiceService)
    {
        _fileService = fileService;
        _invoiceService = invoiceService;
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
            // Try to load from cache if enabled
            if (useCache)
            {
                var cachedContext = await _fileService.LoadCachedContextAsync(filePath, artifactsDirectory);
                if (cachedContext != null)
                {
                    result.Context = cachedContext;
                    result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(cachedContext, Newtonsoft.Json.Formatting.Indented);
                    result.WasCached = true;
                    return result;
                }
            }
            
            // No cache or cache disabled - run pipeline
            var config = PipelineConfigLoader.Load(_defaultPipelineConfigPath);
            var executor = new PipelineExecutor(_invoiceService);
            var context = new PipelineContext
            {
                InputPath = filePath
            };
            
            var processedContext = await executor.RunAsync(config, context);
            
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
        // Load the existing context to preserve other data
        var context = await _fileService.LoadCachedContextAsync(originalFilePath, artifactsDirectory);
        
        if (context != null)
        {
            // Update the Layout property with the validated invoice
            // The Layout can be either JArray or List<InvoiceDto>
            if (context.Layout is JArray)
            {
                context.Layout = new List<InvoiceDto> { invoice };
            }
            else if (context.Layout is List<InvoiceDto> list)
            {
                if (list.Count > 0)
                    list[0] = invoice;
                else
                    list.Add(invoice);
            }
            else
            {
                context.Layout = new List<InvoiceDto> { invoice };
            }
            
            // Save back to file
            await _fileService.SaveContextAsync(originalFilePath, context, artifactsDirectory);
        }
        else
        {
            // No existing context - create a new one
            var newContext = new PipelineContext
            {
                InputPath = originalFilePath,
                Layout = new List<InvoiceDto> { invoice }
            };
            
            await _fileService.SaveContextAsync(originalFilePath, newContext, artifactsDirectory);
        }
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
}
