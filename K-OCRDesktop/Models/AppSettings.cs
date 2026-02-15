namespace K_OCRDesktop.Models;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? ProjectDirectory { get; set; }
    public string? ProjectArtifacts { get; set; }
    public int MaxConcurrentRequests { get; set; } = 3;
}
