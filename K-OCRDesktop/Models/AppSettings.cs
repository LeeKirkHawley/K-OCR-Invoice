namespace K_OCRDesktop.Models;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? DefaultStartDirectory { get; set; }
}
