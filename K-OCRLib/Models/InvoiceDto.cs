namespace K_OCRLib.Models
{
    public sealed class BoundingBoxDto
    {
        public List<float> Points { get; init; } = new(); // Polygon points [x1, y1, x2, y2, x3, y3, x4, y4]
        public int PageNumber { get; init; } = 1;
    }

    public sealed class InvoiceItemDto
    {
        public string Description { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
        public string? TaxRate { get; set; }
        public List<BoundingBoxDto> BoundingBoxes { get; init; } = new();

        /// <summary>
        /// Azure Document Intelligence confidence score for each extracted field.
        /// Key = property name (e.g. nameof(Description)).
        /// Value = confidence 0.0 – 1.0 as returned by Azure.
        /// Fields absent were not extracted by Azure.
        /// </summary>
        public Dictionary<string, double> FieldConfidences { get; set; } = new();

        /// <summary>
        /// Per-field confidence validation result.
        /// Key = property name of the field (e.g. nameof(Description)).
        /// Value = true if the field's Azure confidence meets the configured minimum;
        ///         false if the confidence is below the threshold and the field is flagged.
        /// Fields absent from this dictionary were not checked.
        /// </summary>
        public Dictionary<string, bool> ConfidenceConfirmed { get; set; } = new();

        /// <summary>
        /// Per-field Tesseract cross-validation result.
        /// Key = property name of the field (e.g. nameof(Description)).
        /// Value = true if the field value was found in the Tesseract OCR text (confirmed);
        ///         false if it was not found and is therefore flagged as suspect.
        /// Fields absent from this dictionary were not checked (Azure returned no value).
        /// </summary>
        public Dictionary<string, bool> TesseractConfirmed { get; set; } = new();
    }

    public sealed class InvoiceDto
    {
        public string VendorName { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public string InvoiceId { get; init; } = string.Empty;
        public string InvoiceDate { get; init; } = string.Empty;
        public string DueDate { get; init; } = string.Empty;
        public string PurchaseOrder { get; init; } = string.Empty;
        public decimal? Subtotal { get; init; }
        public decimal? TotalTax { get; init; }
        public decimal? Discount { get; init; }
        public decimal? Total { get; init; }
        public List<InvoiceItemDto> Items { get; init; } = new();
        
        // Bounding boxes for each field
        public Dictionary<string, List<BoundingBoxDto>> FieldBoundingBoxes { get; init; } = new();

        /// <summary>
        /// Azure Document Intelligence confidence score for each extracted header field.
        /// Key = property name (e.g. nameof(InvoiceDto.VendorName)).
        /// Value = confidence 0.0 – 1.0 as returned by Azure.
        /// Fields absent were not extracted by Azure.
        /// </summary>
        public Dictionary<string, double> FieldConfidences { get; init; } = new();
        
        // Original page dimensions from Azure OCR (in inches)
        public double OriginalPageWidth { get; init; }
        public double OriginalPageHeight { get; init; }
        
        // Number of pages in the source document (from Azure result.Pages.Count)
        public int PageCount { get; init; } = 1;

        /// <summary>
        /// Per-field Tesseract cross-validation result.
        /// Key = property name of the field (e.g. nameof(InvoiceDto.VendorName)).
        /// Value = true if the field value was found in the Tesseract OCR text (confirmed);
        ///         false if it was not found and is therefore flagged as suspect.
        /// Fields absent from this dictionary were not checked (Azure returned no value).
        /// </summary>
        public Dictionary<string, bool> TesseractConfirmed { get; set; } = new();

        /// <summary>
        /// Per-field confidence validation result.
        /// Key = property name of the field (e.g. nameof(InvoiceDto.VendorName)).
        /// Value = true if the field's Azure confidence meets the configured minimum;
        ///         false if the confidence is below the threshold and the field is flagged.
        /// Fields absent from this dictionary were not checked.
        /// </summary>
        public Dictionary<string, bool> ConfidenceConfirmed { get; set; } = new();

        /// <summary>
        /// Per-field mathematical validation result.
        /// Key = field identifier (e.g. "Subtotal", "Total", "LineItem[0].Amount").
        /// Value = true if the mathematical calculation is correct (within tolerance);
        ///         false if the math doesn't add up and is therefore flagged as suspect.
        /// Fields absent from this dictionary were not checked.
        /// </summary>
        public Dictionary<string, bool> MathConfirmed { get; set; } = new();
        
        /// <summary>
        /// Indicates whether the user has manually accepted this invoice's validation.
        /// When true, validation checks will be skipped and no validation indicators will be shown.
        /// </summary>
        public bool IsValidationAccepted { get; set; }

        /// <summary>Free-text notes entered by the user for this invoice.</summary>
        public string? Notes { get; set; }

        /// <summary>Detected vendor country of origin (e.g. "United States").</summary>
        public string? VendorCountry { get; set; }

        /// <summary>ISO 4217 currency code detected for this invoice (e.g. "USD", "EUR").</summary>
        public string? CurrencyCode { get; set; }
        
    }
}