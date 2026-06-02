using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

/// <summary>
/// Validates the Azure Document Intelligence confidence score for each
/// extracted invoice field.  Fields whose confidence is strictly below the
/// supplied minimum threshold are flagged by setting their entry in
/// <see cref="InvoiceDto.ConfidenceConfirmed"/> /
/// <see cref="InvoiceItemDto.ConfidenceConfirmed"/> to <c>false</c>.
/// </summary>
public interface IConfidenceValidationService
{
    /// <summary>
    /// Validates every field confidence in <paramref name="invoice"/> against
    /// <paramref name="minConfidenceThreshold"/> and populates
    /// <see cref="InvoiceDto.ConfidenceConfirmed"/> and each
    /// <see cref="InvoiceItemDto.ConfidenceConfirmed"/>.
    /// Fields whose confidence was not reported by Azure (i.e. absent from
    /// <see cref="InvoiceDto.FieldConfidences"/>) are not written to the
    /// dictionary and therefore do not trigger a validation failure.
    /// </summary>
    /// <param name="invoice">The Azure-extracted invoice to validate.</param>
    /// <param name="minConfidenceThreshold">
    /// The minimum acceptable confidence level (0.0 – 1.0).
    /// Fields with a score strictly below this value are flagged.
    /// </param>
    void ValidateConfidence(InvoiceDto invoice, double minConfidenceThreshold);
}
