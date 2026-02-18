using K_OCR.Models;

namespace K_OCR.Services;

public interface ILineItemValidationService
{
    /// <summary>
    /// Validates mathematical accuracy of line items and invoice totals.
    /// Populates the MathConfirmed dictionary on the invoice with validation results.
    /// </summary>
    void ValidateInvoiceMath(InvoiceDto invoice);
}
