namespace K_OCR.Configuration;

/// <summary>
/// Per-organisation configuration stored as OrgConfig.json in the org's folder.
/// </summary>
public class OrgConfig
{
    /// <summary>
    /// Minimum Azure Document Intelligence confidence score (0.0 – 1.0) required
    /// for a field to pass confidence validation.
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// When true, the Export button is disabled until every invoice in the batch
    /// has been validated (IsValidationAccepted = true).
    /// </summary>
    public bool RequireBatchValidationForExport { get; set; } = true;
}
