using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models
{
    [Table("DocumentPages")]
    public class DocumentPage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OCRFileId { get; set; }

        [Required]
        public int PageNumber { get; set; }

        [Required]
        [MaxLength(2000)]
        public string PageFilePath { get; set; } = string.Empty;

        public string? OcrText { get; set; }

        public string? JsonData { get; set; }

        public bool IsProcessed { get; set; } = false;

        public DateTime? ProcessedDate { get; set; }

        // Navigation property
        [ForeignKey("OCRFileId")]
        public virtual OCRFile OCRFile { get; set; } = null!;
    }
}
