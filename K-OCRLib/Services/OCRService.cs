using System.Diagnostics;
using Tesseract;

namespace K_OCR.Services
{
    public class OCRService : IOCRService
    {
        IAnalysisService _analysisService;

        public OCRService(IAnalysisService analysisService)
        {
            _analysisService = analysisService;
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
                    Debug.WriteLine($"OCR failed for {Path.GetFileName(filePath)}: {ex.Message}");
                }
            });
        }
    }
}