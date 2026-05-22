using K_OCR.Models;

namespace K_OCR.Models
{
    public class PipelineContext
    {
        public string InputPath { get; set; }
        public string? TesseractOcrText { get; set; }
        public List<InvoiceDto>? Layout { get; set; }

        /// <summary>
        /// Optional directory used to store per-page PNG images when the input is a PDF.
        /// Passed to <see cref="K_OCR.Services.ITesseractValidationService.ExtractTextAsync"/>.
        /// </summary>
        public string? ArtifactsDirectory { get; set; }

        /// <summary>
        /// Optional per-run confidence threshold override.
        /// When null the value is read from <c>MinConfidenceThreshold</c> in configuration.
        /// </summary>
        public double? MinConfidenceThreshold { get; set; }
    }
}
