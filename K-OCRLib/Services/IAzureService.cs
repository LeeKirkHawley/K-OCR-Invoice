namespace K_OCR.Services
{
    public interface IAzureService
    {
        Task RunAzureOcrAsync(IEnumerable<string> filePaths, string? artifactsDirectory = null);
    }
}
