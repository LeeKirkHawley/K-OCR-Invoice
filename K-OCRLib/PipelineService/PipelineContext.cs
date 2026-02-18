using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.PipelineService
{
    public class PipelineContext
    {
        public string InputPath { get; set; }
        public string Text { get; set; }
        public object Layout { get; set; }
        public object Table { get; set; }
        public object LineItems { get; set; }

        /// <summary>
        /// Raw text extracted by Tesseract (local secondary OCR) for validation
        /// against the primary Azure OCR result.
        /// </summary>
        public string? TesseractOcrText { get; set; }
    }
}
