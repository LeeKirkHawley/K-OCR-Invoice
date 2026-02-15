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

        // Navigation properties for related OCR blocks (not stored in DB)
        [NotMapped]
        public List<OcrBlock> LineBlocks { get; set; } = new List<OcrBlock>();
        
        [NotMapped]
        public List<OcrBlock> TableBlocks { get; set; } = new List<OcrBlock>();
    }
}
