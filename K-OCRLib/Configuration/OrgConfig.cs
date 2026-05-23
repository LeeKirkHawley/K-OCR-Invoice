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

    /// <summary>
    /// Selects which OCR workflow to use for this organisation.
    /// Must match one of the registered <c>IQueuedOcrWorkflow.WorkflowKey</c> values.
    /// Defaults to "Default" (Azure + Tesseract parallel pipeline).
    /// </summary>
    public string OcrWorkflowKey { get; set; } = "Default";
}
