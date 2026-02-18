namespace K_OCR.Models
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
        public List<BoundingBoxDto> BoundingBoxes { get; init; } = new();

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
        public decimal? Shipping { get; init; }
        public decimal? Total { get; init; }
        public List<InvoiceItemDto> Items { get; init; } = new();
        
        // Bounding boxes for each field
        public Dictionary<string, List<BoundingBoxDto>> FieldBoundingBoxes { get; init; } = new();
        
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
        
        /// <summary>
        /// Indicates whether the user has edited this invoice and all validations now pass.
        /// When true, the invoice was edited by the user and has no validation errors.
        /// </summary>
        public bool IsEditedByUser { get; set; }
    }
}