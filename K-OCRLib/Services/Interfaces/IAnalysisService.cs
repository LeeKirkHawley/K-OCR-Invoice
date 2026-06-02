namespace K_OCRLib.Services.Interfaces
{
    public interface IAnalysisService
    {
        void AnalyzePage(Tesseract.Page page, out List<OcrBlock> lineBlocks, out List<OcrBlock> tableBlocks);
    }
}
