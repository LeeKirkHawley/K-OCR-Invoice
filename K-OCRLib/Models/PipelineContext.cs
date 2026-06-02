using K_OCRLib.Identity;
using K_OCRLib.Services.Interfaces;

namespace K_OCRLib.Models
{
    public class PipelineContext
    {
        public string InputPath { get; set; }
        public string? TesseractOcrText { get; set; }
        public List<InvoiceDto>? Layout { get; set; }

        /// <summary>
        /// Optional directory used to store per-page PNG images when the input is a PDF.
        /// Passed to <see cref="ITesseractValidationService.ExtractTextAsync"/>.
        /// </summary>
        public string? ArtifactsDirectory { get; set; }

        /// <summary>
        /// Optional per-run confidence threshold override.
        /// When null the value is read from <c>MinConfidenceThreshold</c> in configuration.
        /// </summary>
        public double? MinConfidenceThreshold { get; set; }

        /// <summary>
        /// The organization on whose behalf the pipeline is running.
        /// Available to pre-run and post-run hooks for per-organization customization.
        /// </summary>
        public Organization? Organization { get; set; }

        /// <summary>
        /// The batch being processed. Available to pre-run and post-run hooks.
        /// </summary>
        public Batch? Batch { get; set; }
    }
}
