namespace OCRQueue.Services;

/// <summary>
/// Configuration for the OCR job queue, bound from the "OcrQueue" section of appsettings.json.
/// </summary>
public sealed class OcrQueueSettings
{
    /// <summary>Maximum number of OCR jobs allowed within one rate-limiter window.</summary>
    public int MaxJobsPerWindow { get; set; } = 10;

    /// <summary>Duration of the rate-limiter window in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Maximum number of OCR jobs that may run concurrently.</summary>
    public int MaxConcurrentJobs { get; set; } = 3;
}
