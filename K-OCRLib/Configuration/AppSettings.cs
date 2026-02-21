namespace K_OCR.Configuration;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? ProjectDirectory { get; set; }
    public string? ProjectArtifacts { get; set; }
    public string? DefaultStartFolder { get; set; }
    public int MaxConcurrentRequests { get; set; } = 3;
    public double? SplitterLeftPaneWidth { get; set; }
    public double? SplitterCenterPaneWidth { get; set; }
    public double? SplitterRightPaneWidth { get; set; }
    public EmailSettings Email { get; set; } = new();

    /// <summary>
    /// Minimum Azure Document Intelligence confidence score (0.0 – 1.0) required
    /// for a field to pass confidence validation.  Fields whose score is strictly
    /// below this value are flagged in <see cref="K_OCR.Models.InvoiceDto.ConfidenceConfirmed"/>.
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.8;

    // Database configuration
    public DatabaseSettings? Database { get; set; }
}

public class DatabaseSettings
{
    public string? ConnectionString { get; set; } = "Data Source=kocr.db";
    public string? Provider { get; set; } = "SQLite"; // SQLite, SQLServer, PostgreSQL, etc.
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
    public bool UseWalMode { get; set; } = false; // WAL mode can cause issues on external drives
}

public class EmailSettings
{
    public bool BypassEmail { get; set; } = true;
    public string? SmtpHost { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; } = "no-reply@kocr.local";
    public string? FromName { get; set; } = "K-OCR";
}
