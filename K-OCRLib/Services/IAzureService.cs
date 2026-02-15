using K_OCR.Models;

namespace K_OCR.Services
{
    public interface IAzureService
    {
        public Task RunAzureOcrAsync(IEnumerable<OCRFile> items, string? artifactsDirectory = null);
    }
}
