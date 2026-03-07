namespace K_OCR.Services
{
    public interface IOCRService
    {
        public Task RunOcrAsync(IEnumerable<string> filePaths);
    }
}
