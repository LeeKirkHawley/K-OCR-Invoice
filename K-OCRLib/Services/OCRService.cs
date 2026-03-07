using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace K_OCR.Services
{
    public class OCRService : IOCRService
    {
        IAnalysisService _analysisService;
        private readonly ILogger<OCRService> _logger;

        public OCRService(IAnalysisService analysisService, ILogger<OCRService> logger)
        {
            _analysisService = analysisService;
            _logger = logger;
        }

        public async Task RunOcrAsync(IEnumerable<string> filePaths)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

            await Parallel.ForEachAsync(filePaths, options, async (filePath, ct) =>
            {
                try
                {
                    var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                    var img = Pix.LoadFromFile(filePath);
                    using (var page = engine.Process(img))
                    {
                        _analysisService.AnalyzePage(page, out var lineBlocks, out var tableBlocks);
                        Debug.WriteLine($"OCR'd {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OCR failed for {FileName}.", Path.GetFileName(filePath));
                }
            });
        }
    }
}