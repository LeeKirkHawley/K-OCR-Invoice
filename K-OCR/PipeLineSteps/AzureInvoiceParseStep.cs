using K_OCR.Services;

namespace K_OCR.PipeLineSteps
{
    public class AzureInvoiceParseStep : IPipelineStep
    {
        private readonly IInvoiceService _invoiceService;

        public AzureInvoiceParseStep(IInvoiceService invoiceService = null)
        {
            _invoiceService = invoiceService ?? new InvoiceService();
        }

        public async Task<PipelineContext> ExecuteAsync(
            PipelineContext context,
            Dictionary<string, string> parameters)
        {
            var engine = parameters.GetValueOrDefault("engine", "azureinvoiceparse");
            var lang = parameters.GetValueOrDefault("language", "eng");

            // Use Azure Invoice Parse service to analyze the document
            var invoiceResults = await _invoiceService.RunAzureInvoiceParse(context.InputPath);
            context.Layout = invoiceResults;
            
            return context;
        }
    }
}
