using K_OCR.Models;
using K_OCR.Services.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public class InvoiceProcessingService : IInvoiceProcessingService
{
    private readonly IFileService _fileService;
    private readonly InvoiceProcessingWorkflow _workflow;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvoiceProcessingService> _logger;

    public InvoiceProcessingService(
        IFileService fileService,
        InvoiceProcessingWorkflow workflow,
        IConfiguration configuration,
        ILogger<InvoiceProcessingService> logger)
    {
        _fileService = fileService;
        _workflow = workflow;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ProcessingResult> ProcessFileAsync(string filePath, string? artifactsDirectory = null, double? minConfidenceThreshold = null)
    {
        var result = new ProcessingResult();
        try
        {
            var context = await _workflow.RunAsync(filePath, artifactsDirectory, minConfidenceThreshold);
            result.Context = context;
            result.Json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FilePath}.", filePath);
            result.Error = ex;
        }
        return result;
    }

    public async Task<Dictionary<string, ProcessingResult>> ProcessBatchAsync(
        IEnumerable<string> filePaths,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        string? artifactsDirectory = null,
        double? minConfidenceThreshold = null,
        Batch? batch = null,
        string? organizationName = null)
    {
        var results = new Dictionary<string, ProcessingResult>();
        var filePathsList = filePaths.ToList();
        var total = filePathsList.Count;
        var completed = 0;

        var batchLabel = batch is not null ? $"{batch.Name} (#{batch.BatchNumber})" : "unknown batch";
        var orgLabel   = organizationName ?? "unknown org";

        var maxConcurrent = Math.Max(1, _configuration.GetValue<int>("MaxConcurrentRequests", 3));
        using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        var tasks = filePathsList.Select(async filePath =>
        {
            await semaphore.WaitAsync();
            var threadId = Environment.CurrentManagedThreadId;
            var fileName = Path.GetFileName(filePath);
            try
            {
                _logger.LogInformation(
                    "OCR thread {ThreadId} started — org: {Org}, batch: {Batch}, file: {File}",
                    threadId, orgLabel, batchLabel, fileName);

                progress?.Report((completed, total, fileName));

                var result = await ProcessFileAsync(filePath, artifactsDirectory, minConfidenceThreshold);

                lock (results)
                {
                    results[filePath] = result;
                }

                Interlocked.Increment(ref completed);
                progress?.Report((completed, total, fileName));

                _logger.LogInformation(
                    "OCR thread {ThreadId} finished — org: {Org}, batch: {Batch}, file: {File}",
                    threadId, orgLabel, batchLabel, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OCR thread {ThreadId} error — org: {Org}, batch: {Batch}, file: {File}",
                    threadId, orgLabel, batchLabel, fileName);
                lock (results)
                {
                    results[filePath] = new ProcessingResult { Error = ex };
                }
            }
            finally
            {
                semaphore.Release();
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

    public async Task<InvoiceDto?> LoadInvoiceAsync(string filePath)
    {
        var context = await _fileService.LoadContextAsync(filePath);
        return context?.Layout?.FirstOrDefault();
    }
}

