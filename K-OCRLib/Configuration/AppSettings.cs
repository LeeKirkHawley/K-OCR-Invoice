namespace K_OCR.Configuration;

public class AppSettings
{
    public string? AzureCognitiveServicesKey { get; set; }
    public string? AzureCognitiveServicesEndpoint { get; set; }
    public string? OCRProvider { get; set; }
    public string? ProjectArtifacts { get; set; }
    public string? DefaultStartFolder { get; set; }
    public int MaxConcurrentRequests { get; set; } = 3;
    public int GuestAccountRetentionDays { get; set; } = 7;
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
    /// <summary>
    /// Connection string for the central identity database (ApplicationDbContext).
    /// Per-organisation OCR databases are routed automatically from the org folder path.
    /// </summary>
    public string? IdentityConnectionString { get; set; } = "Data Source=kocr.db";
    public string? Provider { get; set; } = "SQLite"; // SQLite, SQLServer, PostgreSQL, etc.
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
    public bool UseWalMode { get; set; } = false; // WAL mode can cause issues on external drives
}

public class EmailSettings
{
    public string? SmtpHost { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    /// <summary>
    /// Skip SSL/TLS certificate validation. Use when the SMTP host's certificate
    /// does not match its hostname (e.g. shared hosting providers like Network Solutions/FatCow
    /// whose mail routes through *.smtp.a.cloudfilter.net).
    /// </summary>
    public bool SkipCertificateValidation { get; set; } = false;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; } = "K-OCR";
}
