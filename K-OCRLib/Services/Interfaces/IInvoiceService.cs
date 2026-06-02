using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces
{
    public interface IInvoiceService
    {
        public Task<List<InvoiceDto>> RunAzureInvoiceParse(string imagePath);
        public Task<Dictionary<string, List<InvoiceDto>>> ProcessInvoiceBatchAsync(
            IEnumerable<string> imagePaths, 
            IProgress<(int completed, int total, string currentFile)>? progress = null);
    }
}
