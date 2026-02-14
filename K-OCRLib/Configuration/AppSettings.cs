namespace K_OCR.Configuration;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? DefaultStartDirectory { get; set; }
    public int MaxConcurrentRequests { get; set; } = 3;
    public double? SplitterLeftPaneWidth { get; set; }
    public double? SplitterCenterPaneWidth { get; set; }
    public double? SplitterRightPaneWidth { get; set; }

    // Database configuration
    public DatabaseSettings? Database { get; set; }
}

public class DatabaseSettings
{
    public string? ConnectionString { get; set; } = "Data Source=kocr.db";
    public string? Provider { get; set; } = "SQLite"; // SQLite, SQLServer, PostgreSQL, etc.
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
}
