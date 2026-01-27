using K_OCR.Services;

namespace K_OCR.PipeLineSteps
{
    public class PipelineExecutor
    {
        private readonly Dictionary<string, IPipelineStep> _registry;

        public PipelineExecutor(IInvoiceService invoiceService = null)
        {
            _registry = new Dictionary<string, IPipelineStep>
            {
                ["azureinvoiceparse"] = new AzureInvoiceParseStep(invoiceService),
                //["ocr"] = new OcrStep(),
                //["layout_detection"] = new LayoutDetectionStep(),
                //["table_reconstruction"] = new TableReconstructionStep(),
                //["line_item_extraction"] = new LineItemExtractionStep(),
                //["semantic_cleanup"] = new SemanticCleanupStep()
            };
        }

        public async Task<PipelineContext> RunAsync(
            PipelineTextConfig config,
            PipelineContext context)
        {
            foreach (var step in config.Steps)
            {
                if (!_registry.TryGetValue(step.Step, out var impl))
                    throw new InvalidOperationException($"Unknown step: {step.Step}");

                context = await impl.ExecuteAsync(context, step.Parameters);
            }

            return context;
        }
    }
}
