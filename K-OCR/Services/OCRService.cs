using K_OCR.Models;
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

        public async Task RunOcrAsync(IEnumerable<OCRFile> items)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

            await Parallel.ForEachAsync(items, options, async (ocrFile, ct) =>
            {
                try
                {
                    var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);

                    var img = Pix.LoadFromFile(ocrFile.filePath);
                    using (var page = engine.Process(img))
                    {
                        var text = page.GetText();
                        Debug.WriteLine("Mean confidence: {0}", page.GetMeanConfidence());
                        ocrFile.ocrText = text;

                        List<OcrBlock> lineBlocks, tableBlocks;
                        _analysisService.AnalyzePage(ocrFile, page, out lineBlocks, out tableBlocks);

                        //await Dispatcher.InvokeAsync(() =>
                        //{
                        //    // Build FlowDocument for export only
                        //    var flowDocument = BuildFlowDocument(lineBlocks, tableBlocks);
                        //    ocrFile.Document = flowDocument;

                        //    if (currentIndex >= 0 && currentIndex < filesToProcess.Count)
                        //    {
                        //        var current = filesToProcess[currentIndex];
                        //        if (ReferenceEquals(current, ocrFile))
                        //        {
                        //            DrawOCROverlay(ocrFile);
                        //        }
                        //    }
                        //});

                        Debug.WriteLine($"OCR'd {System.IO.Path.GetFileName(ocrFile.filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    //await Dispatcher.BeginInvoke(() =>
                    //    MessageBox.Show(this, $"OCR failed for {System.IO.Path.GetFileName(ocrFile.filePath)}: {ex.Message}",
                    //        "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }


    }
}
