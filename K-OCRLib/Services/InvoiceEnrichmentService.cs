using System.Globalization;
using System.Text.RegularExpressions;
using K_OCR.Models;

namespace K_OCR.Services;

/// <summary>
/// Enriches a parsed <see cref="InvoiceDto"/> with the vendor's country of origin and the
/// invoice currency code, using <see cref="System.Globalization.RegionInfo"/> /
/// <see cref="CultureInfo"/> for ISO-standard data and pattern-based OCR text scanning.
/// </summary>
public class InvoiceEnrichmentService : IInvoiceEnrichmentService
{
    // ──────────────────────────────────────────────────────────────────────────
    // Static data built once from the .NET globalization database
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a lowercase English country name to its ISO 4217 currency code.
    /// Example: "united states" → "USD"
    /// </summary>
    private static readonly Dictionary<string, string> _countryNameToCurrency;

    /// <summary>
    /// Maps a lowercase English country name to its proper-cased display name.
    /// Example: "united states" → "United States"
    /// </summary>
    private static readonly Dictionary<string, string> _countryNameDisplay;

    /// <summary>
    /// Ordered list of (pattern, ISO-4217 code) pairs used to scan raw OCR text.
    /// Multi-character symbols must appear before their single-character suffixes.
    /// </summary>
    private static readonly (Regex Pattern, string Code)[] _symbolRules;

    /// <summary>
    /// Maps lowercase currency words to ISO 4217 codes.
    /// </summary>
    private static readonly Dictionary<string, string> _wordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dollar"]    = "USD",  ["dollars"]   = "USD",
        ["euro"]      = "EUR",  ["euros"]      = "EUR",
        ["pound"]     = "GBP",  ["pounds"]     = "GBP",  ["sterling"]   = "GBP",
        ["yen"]       = "JPY",
        ["yuan"]      = "CNY",  ["renminbi"]  = "CNY",
        ["rupee"]     = "INR",  ["rupees"]    = "INR",
        ["franc"]     = "CHF",  ["francs"]    = "CHF",
        ["krona"]     = "SEK",  ["kronor"]    = "SEK",
        ["krone"]     = "NOK",  ["kroner"]    = "NOK",
        ["peso"]      = "MXN",  ["pesos"]     = "MXN",
        ["real"]      = "BRL",  ["reais"]     = "BRL",
        ["ruble"]     = "RUB",  ["rubles"]    = "RUB",  ["rouble"]  = "RUB",  ["roubles"]  = "RUB",
        ["won"]       = "KRW",
        ["lira"]      = "TRY",
        ["dirham"]    = "AED",  ["dirhams"]   = "AED",
        ["riyal"]     = "SAR",  ["riyals"]    = "SAR",
        ["zloty"]     = "PLN",  ["zlotych"]   = "PLN",
        ["forint"]    = "HUF",  ["forints"]   = "HUF",
        ["baht"]      = "THB",
        ["dong"]      = "VND",
        ["ringgit"]   = "MYR",
        ["shekel"]    = "ILS",  ["shekels"]   = "ILS",
    };

    // Known ISO 4217 codes for the word-boundary regex filter
    private static readonly HashSet<string> _knownIsoCodes;

    // US state full names (lowercase) and their 2-letter USPS abbreviations.
    // Abbreviations are only matched when followed by a zip code to avoid false positives.
    private static readonly Dictionary<string, string> _usStateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL", ["Alaska"] = "AK", ["Arizona"] = "AZ", ["Arkansas"] = "AR",
        ["California"] = "CA", ["Colorado"] = "CO", ["Connecticut"] = "CT",
        ["Delaware"] = "DE", ["Florida"] = "FL", ["Georgia"] = "GA",
        ["Hawaii"] = "HI", ["Idaho"] = "ID", ["Illinois"] = "IL", ["Indiana"] = "IN",
        ["Iowa"] = "IA", ["Kansas"] = "KS", ["Kentucky"] = "KY", ["Louisiana"] = "LA",
        ["Maine"] = "ME", ["Maryland"] = "MD", ["Massachusetts"] = "MA",
        ["Michigan"] = "MI", ["Minnesota"] = "MN", ["Mississippi"] = "MS",
        ["Missouri"] = "MO", ["Montana"] = "MT", ["Nebraska"] = "NE", ["Nevada"] = "NV",
        ["New Hampshire"] = "NH", ["New Jersey"] = "NJ", ["New Mexico"] = "NM",
        ["New York"] = "NY", ["North Carolina"] = "NC", ["North Dakota"] = "ND",
        ["Ohio"] = "OH", ["Oklahoma"] = "OK", ["Oregon"] = "OR",
        ["Pennsylvania"] = "PA", ["Rhode Island"] = "RI", ["South Carolina"] = "SC",
        ["South Dakota"] = "SD", ["Tennessee"] = "TN", ["Texas"] = "TX",
        ["Utah"] = "UT", ["Vermont"] = "VT", ["Virginia"] = "VA",
        ["Washington"] = "WA", ["West Virginia"] = "WV", ["Wisconsin"] = "WI",
        ["Wyoming"] = "WY",
        // DC and territories
        ["District of Columbia"] = "DC", ["Puerto Rico"] = "PR", ["Guam"] = "GU",
        ["Virgin Islands"] = "VI", ["American Samoa"] = "AS",
        ["Northern Mariana Islands"] = "MP",
    };

    // 2-letter abbreviations valid when followed by a US zip code (5 or 9 digits).
    private static readonly HashSet<string> _usStateAbbreviations =
        new(_usStateNames.Values, StringComparer.OrdinalIgnoreCase);

    // Matches: optional comma/space, 2-letter abbreviation, space, 5-digit zip (optionally -4)
    private static readonly Regex _usAddressPattern = new(
        @"[,\s]([A-Z]{2})\s+\d{5}(?:-\d{4})?(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static InvoiceEnrichmentService()
    {
        var nameToCode    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameToDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var isoCodes      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region      = new RegionInfo(culture.Name);
                var currCode    = region.ISOCurrencySymbol;
                var countryName = region.EnglishName;

                isoCodes.Add(currCode);

                // First registration wins (avoids nondeterministic overwriting)
                nameToCode.TryAdd(countryName, currCode);
                nameToDisplay.TryAdd(countryName, countryName);
            }
            catch
            {
                // Some culture names are not valid for RegionInfo; skip them silently.
            }
        }

        _countryNameToCurrency = nameToCode;
        _countryNameDisplay    = nameToDisplay;
        _knownIsoCodes         = isoCodes;

        // Symbol → currency rules (multi-char first to avoid partial matches)
        _symbolRules = new (Regex, string)[]
        {
            (BuildSymbol(@"CA\$"),  "CAD"),
            (BuildSymbol(@"A\$"),   "AUD"),
            (BuildSymbol(@"NZ\$"),  "NZD"),
            (BuildSymbol(@"HK\$"),  "HKD"),
            (BuildSymbol(@"S\$"),   "SGD"),
            (BuildSymbol(@"\$"),    "USD"),
            (BuildSymbol(@"£"),     "GBP"),
            (BuildSymbol(@"€"),     "EUR"),
            (BuildSymbol(@"¥"),     "JPY"),
            (BuildSymbol(@"₹"),     "INR"),
            (BuildSymbol(@"₩"),     "KRW"),
            (BuildSymbol(@"₺"),     "TRY"),
            (BuildSymbol(@"₽"),     "RUB"),
            (BuildSymbol(@"¢"),     "USD"),
            (BuildSymbol(@"₴"),     "UAH"),
            (BuildSymbol(@"₦"),     "NGN"),
            (BuildSymbol(@"R\$"),   "BRL"),
        };

        static Regex BuildSymbol(string escaped) =>
            new(escaped, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void DetectCountryAndCurrency(InvoiceDto invoice, string? ocrText)
    {
        // Build a combined text blob from OCR text and key structured fields so that
        // detection works even when Tesseract produced nothing.
        var combinedText = BuildSearchText(invoice, ocrText);

        // ── Currency detection ──────────────────────────────────────────────
        if (string.IsNullOrEmpty(invoice.CurrencyCode))
        {
            invoice.CurrencyCode = DetectCurrencyCode(combinedText);
        }

        // ── Country detection ───────────────────────────────────────────────
        // Country is detected only from explicit OCR evidence (country name in text/address).
        // We deliberately do NOT infer country from currency because many currencies are
        // shared (EUR: 20+ countries; USD: used as official or peg currency in many more).
        if (string.IsNullOrEmpty(invoice.VendorCountry))
        {
            invoice.VendorCountry = DetectCountryName(combinedText)
                                 ?? DetectUsStateCountry(combinedText);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildSearchText(InvoiceDto invoice, string? ocrText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ocrText))          parts.Add(ocrText);
        if (!string.IsNullOrWhiteSpace(invoice.VendorName))   parts.Add(invoice.VendorName);
        if (!string.IsNullOrWhiteSpace(invoice.CustomerName)) parts.Add(invoice.CustomerName);
        return string.Join(" ", parts);
    }

    private static string? DetectCurrencyCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Explicit 3-letter ISO codes at word boundaries (uppercase only in OCR)
        var isoMatch = Regex.Match(text, @"\b([A-Z]{3})\b");
        while (isoMatch.Success)
        {
            if (_knownIsoCodes.Contains(isoMatch.Groups[1].Value))
                return isoMatch.Groups[1].Value;
            isoMatch = isoMatch.NextMatch();
        }

        // 2. Currency symbols (multi-char before single-char)
        foreach (var (pattern, code) in _symbolRules)
        {
            if (pattern.IsMatch(text)) return code;
        }

        // 3. Currency words (case-insensitive)
        foreach (var (word, code) in _wordMap)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                return code;
        }

        return null;
    }

    private static string? DetectCountryName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Scan for known country names; prefer longer matches to avoid "Chad" matching "Richard".
        // Require at least 4 characters to avoid single-word false positives.
        string? best = null;
        foreach (var kvp in _countryNameDisplay)
        {
            var name = kvp.Value;
            if (name.Length < 4) continue;

            if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (best == null || name.Length > best.Length)
                    best = name;
            }
        }
        return best;
    }

    /// <summary>
    /// Returns "United States" if the text contains a US state full name or a 2-letter USPS
    /// abbreviation followed by a zip code (e.g. "San Jose, CA 95110").
    /// Returns <c>null</c> if no US state evidence is found.
    /// </summary>
    private static string? DetectUsStateCountry(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Full state names — distinctive enough to match without further context.
        foreach (var name in _usStateNames.Keys)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
                return "United States";
        }

        // 2-letter abbreviations only when followed by a zip code to reduce false positives.
        var match = _usAddressPattern.Match(text);
        while (match.Success)
        {
            if (_usStateAbbreviations.Contains(match.Groups[1].Value))
                return "United States";
            match = match.NextMatch();
        }

        return null;
    }
}
