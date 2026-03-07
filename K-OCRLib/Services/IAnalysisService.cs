namespace K_OCR.Services
{
    public interface IAnalysisService
    {
        void AnalyzePage(Tesseract.Page page, out List<OcrBlock> lineBlocks, out List<OcrBlock> tableBlocks);
    }
}
