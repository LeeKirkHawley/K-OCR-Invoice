using K_OCR.Models;

namespace KOCRAsp.Models;

public class SaveInvoiceRequest
{
    public string FilePath { get; set; } = string.Empty;
    public InvoiceDto? Invoice { get; set; }
}
