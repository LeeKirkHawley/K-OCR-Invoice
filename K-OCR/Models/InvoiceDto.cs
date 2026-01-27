namespace K_OCR.Models
{
    public sealed class InvoiceItemDto
    {
        public string Description { get; init; } = string.Empty;
        public decimal? Quantity { get; init; }
        public decimal? UnitPrice { get; init; }
        public decimal? LineTotal { get; init; }
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
    }
}