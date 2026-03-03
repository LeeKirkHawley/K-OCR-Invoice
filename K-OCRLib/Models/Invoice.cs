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

        [Required]
        [MaxLength(500)]
        public string VendorName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string InvoiceId { get; set; } = string.Empty;

        public DateTime? InvoiceDate { get; set; }

        public DateTime? DueDate { get; set; }

        [MaxLength(100)]
        public string PurchaseOrder { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Subtotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalTax { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Shipping { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Total { get; set; }

        // File information
        [Required]
        [MaxLength(1000)]
        public string FilePath { get; set; } = string.Empty;

        public DateTime ProcessedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tenant that owns this record. Null only for records created before multi-tenancy
        /// was introduced. Populated by <c>DatabaseService</c> from the current
        /// <c>ITenantContext</c> on every insert; filtered by <c>KOCRDbContext</c>
        /// global query filter in Step 7.
        /// </summary>
        [MaxLength(450)]
        public string? OrganizationId { get; set; }

        // Navigation properties
        public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        public virtual ICollection<DocumentField> DocumentFields { get; set; } = new List<DocumentField>();
    }
}