using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

public interface ILineItemValidationService
{
    /// <summary>
    /// Validates mathematical accuracy of line items and invoice totals.
    /// Populates the MathConfirmed dictionary on the invoice with validation results.
    /// </summary>
    void ValidateInvoiceMath(InvoiceDto invoice);
}
