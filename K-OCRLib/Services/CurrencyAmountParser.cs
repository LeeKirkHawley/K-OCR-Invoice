using System.Text.RegularExpressions;

namespace K_OCR.Services;

/// <summary>
/// Pure-static helper that normalises raw OCR currency strings into <see cref="decimal"/> values.
/// Separating this from <see cref="InvoiceEnrichmentService"/> lets callers that are not
/// DI-injected (e.g. static helpers inside <see cref="InvoiceService"/>) use it without coupling.
/// </summary>
public static class CurrencyAmountParser
{
    // Matches a string that looks like a monetary amount, optionally preceded by a
    // currency symbol or ISO code: e.g. "$1,234.56", "USD 1.234,56", "€1 234.00"
    private static readonly Regex _moneyPattern = new(
        @"^\s*(?<open>\()?\s*(?<sign1>-)?\s*(?:[A-Z]{2,3}\$?|[$£€¥₹₩₺₽¢₴₦])\s*(?<sign2>-)?\s*(?<num>[\d][\d\s,.']*)\s*(?<close>\))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Attempts to parse a raw OCR currency string such as "$1,234.56" or "1.234,56 EUR"
    /// into a <see cref="decimal"/> value.  Returns <c>null</c> if the input cannot be
    /// recognised as a plausible monetary amount.
    /// </summary>
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();

        var match = _moneyPattern.Match(trimmed);
        if (!match.Success) return null;
        if (match.Groups["open"].Success != match.Groups["close"].Success) return null;

        var digits = match.Groups["num"].Value;
        if (string.IsNullOrEmpty(digits)) return null;

        // Remove internal whitespace (e.g. "1 234.56")
        digits = Regex.Replace(digits, @"\s+", string.Empty);

        bool hasComma  = digits.Contains(',');
        bool hasPeriod = digits.Contains('.');

        string normalised;

        if (hasComma && hasPeriod)
        {
            // Both separators present: rightmost is the decimal separator.
            int lastComma  = digits.LastIndexOf(',');
            int lastPeriod = digits.LastIndexOf('.');

            if (lastComma > lastPeriod)
            {
                // "1.234,56" → European format
                normalised = digits.Replace(".", string.Empty).Replace(',', '.');
            }
            else
            {
                // "1,234.56" → US/UK format
                normalised = digits.Replace(",", string.Empty);
            }
        }
        else if (hasComma)
        {
            // Only comma: if exactly 3 digits follow it, treat as thousands separator.
            int commaIdx = digits.LastIndexOf(',');
            int afterComma = digits.Length - commaIdx - 1;

            normalised = afterComma == 3
                ? digits.Replace(",", string.Empty)          // thousands: "1,234"
                : digits.Replace(',', '.');                   // decimal:   "1,50"
        }
        else
        {
            // Only period (or no separator): leave as-is.
            normalised = digits;
        }

        var isNegative = match.Groups["open"].Success
            || match.Groups["sign1"].Success
            || match.Groups["sign2"].Success;

        return decimal.TryParse(normalised, System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? (isNegative ? -result : result)
            : null;
    }
}
