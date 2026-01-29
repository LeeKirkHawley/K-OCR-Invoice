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

        // Pre-built document produced during OCR step; used directly on navigation
        public System.Windows.Documents.FlowDocument? Document { get; set; }

        public List<OcrBlock> LineBlocks;
        public List<OcrBlock> TableBlocks;

    }
}
