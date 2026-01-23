using K_OCR.Models;

namespace K_OCR.Services
{
    public interface IInvoiceService
    {
        public Task<List<InvoiceDto>> RunAzureInvoiceParse(string imagePath);
    }
}
