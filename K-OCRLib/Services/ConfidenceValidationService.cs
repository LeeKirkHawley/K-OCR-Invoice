using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCRLib.Services;

/// <summary>
/// Checks every Azure Document Intelligence field confidence score against a
/// configurable minimum threshold.  Any field whose score is strictly below
/// that threshold is flagged by setting its entry in
/// <see cref="InvoiceDto.ConfidenceConfirmed"/> /
/// <see cref="InvoiceItemDto.ConfidenceConfirmed"/> to <c>false</c>.
///
/// Fields for which Azure reported no confidence value are skipped; they do
/// not appear in the dictionary and therefore do not trigger a failure.
/// </summary>
public class ConfidenceValidationService : IConfidenceValidationService
{
    /// <inheritdoc />
    public void ValidateConfidence(InvoiceDto invoice, double minConfidenceThreshold)
    {
        if (invoice == null) 
            return;

        // ── Header fields ────────────────────────────────────────────────
        var headerFields = new[] {
            nameof(InvoiceDto.VendorName),
            nameof(InvoiceDto.CustomerName),
            nameof(InvoiceDto.InvoiceId),
            nameof(InvoiceDto.InvoiceDate),
            nameof(InvoiceDto.DueDate),
            nameof(InvoiceDto.PurchaseOrder),
            nameof(InvoiceDto.Subtotal),
            nameof(InvoiceDto.TotalTax),
            nameof(InvoiceDto.Discount),
            nameof(InvoiceDto.Total)
        };
        foreach (var field in headerFields)
        {
            if (invoice.FieldConfidences.ContainsKey(field))
            {
                Check(invoice.ConfidenceConfirmed, invoice.FieldConfidences, field, minConfidenceThreshold);
            }
        }

        // ── Line items ───────────────────────────────────────────────────
        if (invoice.Items == null)
            return;

        var itemFields = new[] {
            nameof(InvoiceItemDto.Description),
            nameof(InvoiceItemDto.Quantity),
            nameof(InvoiceItemDto.UnitPrice),
            nameof(InvoiceItemDto.Amount),
            nameof(InvoiceItemDto.TaxRate)
        };
        foreach (var item in invoice.Items)
        {
            foreach (var field in itemFields)
            {
                if (item.FieldConfidences.ContainsKey(field))
                {
                    Check(item.ConfidenceConfirmed, item.FieldConfidences, field, minConfidenceThreshold);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Private helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes <c>true</c> or <c>false</c> to <paramref name="confirmed"/> for
    /// <paramref name="fieldName"/> when a confidence value exists in
    /// <paramref name="confidences"/>; otherwise does nothing.
    /// </summary>
    private static void Check(
        Dictionary<string, bool>   confirmed,
        Dictionary<string, double> confidences,
        string                     fieldName,
        double                     threshold)
    {
        if (confidences.TryGetValue(fieldName, out double confidence))
        {
            confirmed[fieldName] = confidence >= threshold;
        }
        // No entry → field was not extracted by Azure; skip silently.
    }
}
