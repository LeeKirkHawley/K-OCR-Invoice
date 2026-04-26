namespace K_OCR.Services;

public static class InvoiceActionTypes
{
    public const string Added             = "Added";
    public const string OCRed             = "OCRed";
    public const string Validated         = "Validated";
    public const string Exported          = "Exported";
    public const string MarkedForDeletion = "MarkedForDeletion";
    public const string HardDeleted       = "HardDeleted";
}
