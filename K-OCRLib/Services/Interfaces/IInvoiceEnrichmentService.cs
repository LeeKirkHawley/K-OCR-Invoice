using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

/// <summary>
/// Enriches a parsed <see cref="InvoiceDto"/> with country of origin and currency information
/// that cannot be reliably obtained from the Azure Document Intelligence structured fields alone.
/// </summary>
public interface IInvoiceEnrichmentService
{
    /// <summary>
    /// Detects the vendor's country of origin and the invoice currency code from the supplied
    /// OCR text and any currency code already captured by Azure, then writes the results into
    /// <paramref name="invoice"/>.
    /// </summary>
    /// <param name="invoice">The invoice DTO to enrich in-place.</param>
    /// <param name="ocrText">
    /// Best-available full OCR text (Tesseract preferred; fall back to whatever is available).
    /// May be <c>null</c> or empty — detection will still run against structured fields.
    /// </param>
    void DetectCountryAndCurrency(InvoiceDto invoice, string? ocrText);
}
