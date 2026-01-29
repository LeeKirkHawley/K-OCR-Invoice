using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.Models
{
    public class OCRFile
    {
        public string filePath { get; set; } = string.Empty;
        public string ocrText { get; set; } = string.Empty;

        public List<OcrBlock> LineBlocks = new List<OcrBlock>();
        public List<OcrBlock> TableBlocks = new List<OcrBlock>();

    }
}
