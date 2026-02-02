namespace K_OCR.Configuration;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? DefaultStartDirectory { get; set; }
    public int MaxConcurrentRequests { get; set; } = 3;
}
