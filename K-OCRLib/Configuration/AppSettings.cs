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
    public int DeletedBatchRetentionDays { get; set; } = 14;

    /// <summary>Per-user-type limits for batches, invoices, and pages.</summary>
    public LimitsSection Limits { get; set; } = new();

    // ── Legacy flat properties kept for JSON forward-compat deserialization ──
    // These were migrated into Limits.Guest.*. Reading is now done via Limits.
    // The properties remain so old appsettings.json files still deserialize cleanly.
    public int GuestMaxBatches { get; set; } = 2;

    /// <summary>
    /// When true, a batch is soft-deleted automatically after it is successfully exported
    /// (JSON or Excel). The batch is moved to the deleted queue and purged after
    /// <see cref="DeletedBatchRetentionDays"/> days.
    /// </summary>
    public bool SoftDeleteBatchOnExport { get; set; } = true;
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

/// <summary>Configurable limits for regular users and guest users.</summary>
public class LimitsSection
{
    public UserLimits User { get; set; } = new();
    public GuestLimits Guest { get; set; } = new();
}

public class UserLimits
{
    /// <summary>Maximum invoices per batch for regular users. Defaults to 100.</summary>
    public int MaxInvoicesPerBatch { get; set; } = 100;

    /// <summary>
    /// Maximum pages per invoice for regular users. Invoices exceeding this limit
    /// are grayed out and skipped during OCR. Defaults to 20.
    /// </summary>
    public int MaxPagesPerInvoice { get; set; } = 20;
}

public class GuestLimits
{
    /// <summary>Maximum active batches for guest users. Defaults to 2.</summary>
    public int MaxBatches { get; set; } = 2;

    /// <summary>Maximum invoices per batch for guest users. Defaults to 20.</summary>
    public int MaxInvoicesPerBatch { get; set; } = 20;

    /// <summary>Maximum pages per invoice for guest users. Invoices exceeding this limit
    /// are grayed out and skipped during OCR. Defaults to 20.
    /// </summary>
    public int MaxPagesPerInvoice { get; set; } = 20;
}

public class DatabaseSettings
{
    /// <summary>
    /// Connection string for the central identity database (ApplicationDbContext).
    /// Per-organisation OCR databases are routed automatically from the org folder path.
    /// Defaults to SQLite for safe local fallback; set Provider to "SqlServer" in appsettings
    /// and supply a SQL Server connection string for production use.
    /// </summary>
    public string? ConnectionString { get; set; } = "Data Source=kocr.db";

    /// <summary>Database provider for the identity database: "SQLite" (default) or "SqlServer".</summary>
    public string? Provider { get; set; } = "SQLite";

    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
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

