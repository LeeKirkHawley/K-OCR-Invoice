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
        public decimal? LineTotal { get; set; }
        public List<BoundingBoxDto> BoundingBoxes { get; init; } = new();
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
    }
}