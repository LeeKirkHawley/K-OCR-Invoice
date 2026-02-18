using K_OCR.Models;

namespace K_OCR.Services;

/// <summary>
/// Cross-validates the structured fields extracted by Azure Document Intelligence
/// against the raw text produced by a local Tesseract OCR pass.
/// Each field that cannot be located in the Tesseract text is flagged by setting
/// its entry in <see cref="InvoiceDto.TesseractConfirmed"/> / 
/// <see cref="InvoiceItemDto.TesseractConfirmed"/> to <c>false</c>.
/// </summary>
public interface IInvoiceValidationService
{
    /// <summary>
    /// Validates every extracted field in <paramref name="invoice"/> against
    /// <paramref name="tesseractText"/> and populates
    /// <see cref="InvoiceDto.TesseractConfirmed"/> and each
    /// <see cref="InvoiceItemDto.TesseractConfirmed"/>.
    /// </summary>
    /// <param name="invoice">The Azure-extracted invoice to validate.</param>
    /// <param name="tesseractText">Raw text from the Tesseract secondary OCR pass.</param>
    void ValidateAgainstTesseract(InvoiceDto invoice, string tesseractText);
}
