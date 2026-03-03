using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models
{
    [Table("OcrFiles")]
    public class OCRFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public string OcrText { get; set; } = string.Empty;

        public string? ValidatedOcrText { get; set; }

        /// <summary>
        /// Secondary OCR text produced by Tesseract for validation / cross-referencing with Azure results.
        /// </summary>
        public string? TesseractOcrText { get; set; }

        public int TotalPages { get; set; } = 1;

        public string? MergedJsonData { get; set; }

        public bool IsFullyProcessed { get; set; } = false;

        /// <summary>
        /// Tenant that owns this record. Null only for records created before multi-tenancy
        /// was introduced. Populated by <c>DatabaseService</c> from the current
        /// <c>ITenantContext</c> on every insert; filtered by <c>KOCRDbContext</c>
        /// global query filter in Step 7.
        /// </summary>
        [MaxLength(450)]
        public string? OrganizationId { get; set; }

        // Navigation properties for related OCR blocks (not stored in DB)
        [NotMapped]
        public List<OcrBlock> LineBlocks { get; set; } = new List<OcrBlock>();
        
        [NotMapped]
        public List<OcrBlock> TableBlocks { get; set; } = new List<OcrBlock>();

        // Navigation property for multi-page documents
        public virtual ICollection<DocumentPage> Pages { get; set; } = new List<DocumentPage>();
    }
}
