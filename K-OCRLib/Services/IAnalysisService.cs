using K_OCR.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public interface IAnalysisService
    {
        public void AnalyzePage(OCRFile ocrFile, Tesseract.Page page, out List<OcrBlock> lineBlocks, out List<OcrBlock> tableBlocks);
    }
}
