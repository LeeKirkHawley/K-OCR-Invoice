using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCRLib.Models
{
    [Table("InvoiceItems")]
    public class InvoiceItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int InvoiceId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? LineTotal { get; set; }

        [MaxLength(50)]
        public string? TaxRate { get; set; }

        public string? ItemEdits { get; set; }

        //public string? ItemEdits { get; set; }
        //[NotMapped]
        //public JRaw? ItemEditsAsJRaw
        //{
        //    get => string.IsNullOrEmpty(ItemEdits) ? null : new JRaw(ItemEdits);
        //    set => ItemEdits = value?.ToString();
        //}

        // Navigation property
        [ForeignKey("InvoiceId")]
        public virtual Invoice Invoice { get; set; } = null!;
    }
}