using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models
{
    [Table("Invoices")]
    public class Invoice
    {
        [Key]
        public int Id { get; set; }

        // FK to Batch (required for all new records)
        public int BatchId { get; set; }

        [MaxLength(500)]
        public string? VendorName { get; set; }

        [MaxLength(500)]
        public string? CustomerName { get; set; }

        [MaxLength(100)]
        public string? InvoiceId { get; set; }

        public DateTime? InvoiceDate { get; set; }

        public DateTime? DueDate { get; set; }

        [MaxLength(100)]
        public string? PurchaseOrder { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Subtotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalTax { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Shipping { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Total { get; set; }

        // File information
        [MaxLength(1000)]
        public string? FilePath { get; set; }

        /// <summary>Timestamp when the file was uploaded to the batch.</summary>
        public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Timestamp when OCR processing completed. Null until processed.</summary>
        public DateTime? ProcessedAtUtc { get; set; }

        // OCR data (formerly in OCRFile)
        public string? OcrText { get; set; }
        public string? ValidatedOcrText { get; set; }
        public string? TesseractOcrText { get; set; }
        public int TotalPages { get; set; } = 1;
        public string? MergedJsonData { get; set; }
        public bool IsFullyProcessed { get; set; } = false;
        public bool IsValidationAccepted { get; set; } = false;

        /// <summary>
        /// Tenant that owns this record. Populated by KOCRDbContext.SaveChangesAsync
        /// from the current ITenantContext on every insert.
        /// </summary>
        [MaxLength(450)]
        public string? OrganizationId { get; set; }

        // Navigation properties
        public virtual Batch? Batch { get; set; }
        public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        public virtual ICollection<DocumentField> DocumentFields { get; set; } = new List<DocumentField>();
    }
}
