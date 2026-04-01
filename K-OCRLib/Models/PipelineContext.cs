using K_OCR.Models;

namespace K_OCR.Models
{
    public class PipelineContext
    {
        public string InputPath { get; set; }
        public string? TesseractOcrText { get; set; }
        public List<InvoiceDto>? Layout { get; set; }
    }
}
