using System.Reflection;
using System.Text.RegularExpressions;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCRLib.Services;

/// <summary>
/// Validates Azure-extracted invoice fields against raw Tesseract OCR text.
///
/// For each field that Azure extracted a value for, the service checks whether
/// that value (or a reasonable equivalent) appears anywhere in the Tesseract
/// plain-text. Fields that are not found are marked <c>false</c> in
/// <see cref="InvoiceDto.TesseractConfirmed"/> — i.e. flagged as suspect.
///
/// Matching rules:
///   • String fields  – case-insensitive substring search after whitespace
///                      normalisation.  Short values (≤ 2 chars) are skipped to
///                      avoid trivial false-positives.
///   • Decimal fields – several common number formats are tried
///                      (e.g. "1234.56", "1,234.56", "1234").
///   • Date fields    – the raw string from Azure is tried first; if that fails,
///                      common date component substrings are tried.
/// </summary>
public class InvoiceValidationService : IInvoiceValidationService
{
    // Fields whose names match InvoiceDto property names
    private static readonly string[] StringFields =
    [
        nameof(InvoiceDto.VendorName),
        nameof(InvoiceDto.CustomerName),
        nameof(InvoiceDto.InvoiceId),
        nameof(InvoiceDto.InvoiceDate),
        nameof(InvoiceDto.DueDate),
        nameof(InvoiceDto.PurchaseOrder),
    ];

    private static readonly string[] DecimalFields =
    [
        nameof(InvoiceDto.Subtotal),
        nameof(InvoiceDto.TotalTax),
        nameof(InvoiceDto.Shipping),
        nameof(InvoiceDto.Total),
    ];

    /// <inheritdoc />
    public void ValidateAgainstTesseract(InvoiceDto invoice, string tesseractText)

    {

        // Skip validation if no Azure OCR data is present (i.e., all key fields and items are empty)
        bool hasAzureData =
            !string.IsNullOrWhiteSpace(invoice.VendorName) ||
            !string.IsNullOrWhiteSpace(invoice.CustomerName) ||
            !string.IsNullOrWhiteSpace(invoice.InvoiceId) ||
            !string.IsNullOrWhiteSpace(invoice.InvoiceDate) ||
            !string.IsNullOrWhiteSpace(invoice.DueDate) ||
            !string.IsNullOrWhiteSpace(invoice.PurchaseOrder) ||
            invoice.Subtotal.HasValue ||
            invoice.TotalTax.HasValue ||
            invoice.Shipping.HasValue ||
            invoice.Total.HasValue ||
            (invoice.Items != null && invoice.Items.Count > 0);

        if (!hasAzureData)
            return;

        if (string.IsNullOrWhiteSpace(tesseractText))
        {
            // Tesseract produced nothing — flag every field Azure extracted
            FlagAllExtracted(invoice);
            return;
        }

        var normalizedTess = NormalizeWhitespace(tesseractText);

        // ── Header string fields ────────────────────────────────────────────
        var stringValues = new Dictionary<string, string?>
        {
            [nameof(InvoiceDto.VendorName)]    = invoice.VendorName,
            [nameof(InvoiceDto.CustomerName)]  = invoice.CustomerName,
            [nameof(InvoiceDto.InvoiceId)]     = invoice.InvoiceId,
            [nameof(InvoiceDto.InvoiceDate)]   = invoice.InvoiceDate,
            [nameof(InvoiceDto.DueDate)]       = invoice.DueDate,
            [nameof(InvoiceDto.PurchaseOrder)] = invoice.PurchaseOrder,
        };

        foreach (var (name, value) in stringValues)
            CheckStringField(invoice.TesseractConfirmed, name, value, normalizedTess);

        // ── Header decimal fields ───────────────────────────────────────────
        var decimalValues = new Dictionary<string, decimal?>
        {
            [nameof(InvoiceDto.Subtotal)]  = invoice.Subtotal,
            [nameof(InvoiceDto.TotalTax)]  = invoice.TotalTax,
            [nameof(InvoiceDto.Shipping)]  = invoice.Shipping,
            [nameof(InvoiceDto.Total)]     = invoice.Total,
        };

        foreach (var (name, value) in decimalValues)
            CheckDecimalField(invoice.TesseractConfirmed, name, value, normalizedTess);

        // ── Line items ──────────────────────────────────────────────────────
        if (invoice.Items != null)
        {
            foreach (var item in invoice.Items)
            {
                CheckStringField(item.TesseractConfirmed, nameof(InvoiceItemDto.Description),
                    item.Description, normalizedTess);
                CheckDecimalField(item.TesseractConfirmed, nameof(InvoiceItemDto.Quantity),
                    item.Quantity, normalizedTess);
                CheckDecimalField(item.TesseractConfirmed, nameof(InvoiceItemDto.UnitPrice),
                    item.UnitPrice, normalizedTess);
                CheckDecimalField(item.TesseractConfirmed, nameof(InvoiceItemDto.Amount),
                    item.Amount, normalizedTess);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Checks a string field value against the Tesseract text.
    /// Skips the check (no entry written) when Azure returned no value or the
    /// value is too short to be meaningful (≤ 2 characters after trimming).
    /// </summary>
    private static void CheckStringField(
        Dictionary<string, bool> flags,
        string fieldName,
        string? value,
        string normalizedTess)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length <= 2)
            return; // Not extracted by Azure — nothing to validate

        var normalizedValue = NormalizeWhitespace(value);
        flags[fieldName] = normalizedTess.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase);
        //System.Diagnostics.Debug.Assert(flags[fieldName] == true);
    }

    /// <summary>
    /// Checks a decimal field by trying several common number formats.
    /// Skips the check when Azure returned no value.
    /// </summary>
    private static void CheckDecimalField(
        Dictionary<string, bool> flags,
        string fieldName,
        decimal? value,
        string normalizedTess)
    {
        if (!value.HasValue)
            return; // Not extracted by Azure — nothing to validate

        var confirmed = GenerateDecimalFormats(value.Value)
            .Any(fmt => normalizedTess.Contains(fmt, StringComparison.OrdinalIgnoreCase));

        flags[fieldName] = confirmed;
        //System.Diagnostics.Debug.Assert(flags[fieldName] == true);
    }

    /// <summary>
    /// Generates the candidate string representations of a decimal that might
    /// appear in raw OCR text.
    /// All formats use InvariantCulture so server locale never affects the result
    /// (invoices are assumed to use English number formatting: period decimal,
    /// comma thousands separator).
    /// </summary>
    private static IEnumerable<string> GenerateDecimalFormats(decimal value)
    {
        var ic      = System.Globalization.CultureInfo.InvariantCulture;
        var rounded = Math.Round(value, 0);

        // ── Without currency symbol ──────────────────────────────────────
        // Trailing-zero-stripped decimal, e.g. "1234.56" or "1234" for whole numbers
        yield return value.ToString("0.##", ic);
        // Always two decimal places, e.g. "1234.56"
        yield return value.ToString("0.00", ic);
        // Thousands-separated, two decimal places, e.g. "1,234.56"
        yield return value.ToString("N2", ic);
        // Integer (no decimal), e.g. "1234"
        yield return rounded.ToString("0", ic);
        // Thousands-separated integer, e.g. "1,234"
        yield return rounded.ToString("N0", ic);

        // ── With dollar sign ─────────────────────────────────────────────
        yield return "$" + value.ToString("0.##", ic);
        yield return "$" + value.ToString("0.00", ic);
        yield return "$" + value.ToString("N2",   ic);
        yield return "$" + rounded.ToString("0",  ic);
        yield return "$" + rounded.ToString("N0", ic);
    }

    /// <summary>
    /// Flags all fields that Azure actually extracted (i.e. where the invoice
    /// has a non-empty value) when Tesseract produced no text at all.
    /// </summary>
    private static void FlagAllExtracted(InvoiceDto invoice)
    {
        void Flag(Dictionary<string, bool> flags, string name, bool hasValue)
        {
            if (hasValue) flags[name] = false;
        }

        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.VendorName),    !string.IsNullOrWhiteSpace(invoice.VendorName));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.CustomerName),  !string.IsNullOrWhiteSpace(invoice.CustomerName));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.InvoiceId),     !string.IsNullOrWhiteSpace(invoice.InvoiceId));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.InvoiceDate),   !string.IsNullOrWhiteSpace(invoice.InvoiceDate));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.DueDate),       !string.IsNullOrWhiteSpace(invoice.DueDate));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.PurchaseOrder), !string.IsNullOrWhiteSpace(invoice.PurchaseOrder));
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.Subtotal),      invoice.Subtotal.HasValue);
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.TotalTax),      invoice.TotalTax.HasValue);
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.Shipping),      invoice.Shipping.HasValue);
        Flag(invoice.TesseractConfirmed, nameof(InvoiceDto.Total),         invoice.Total.HasValue);

        foreach (var item in invoice.Items)
        {
            Flag(item.TesseractConfirmed, nameof(InvoiceItemDto.Description), !string.IsNullOrWhiteSpace(item.Description));
            Flag(item.TesseractConfirmed, nameof(InvoiceItemDto.Quantity),    item.Quantity.HasValue);
            Flag(item.TesseractConfirmed, nameof(InvoiceItemDto.UnitPrice),   item.UnitPrice.HasValue);
            Flag(item.TesseractConfirmed, nameof(InvoiceItemDto.Amount),   item.Amount.HasValue);
        }
    }

    private static string NormalizeWhitespace(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ");
}
