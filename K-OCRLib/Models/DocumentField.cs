using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCRLib.Models;

[Table("DocumentFields")]
public class DocumentField
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int InvoiceId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(50)]
    public string FieldType { get; set; } = "Text"; // Text, Currency, Date, Number, List

    // Navigation property
    [ForeignKey("InvoiceId")]
    public virtual Invoice Invoice { get; set; } = null!;

    // UI properties (not stored in database)
    [NotMapped]
    public object? RawValue { get; set; }

    [NotMapped]
    public List<BoundingBoxDto>? BoundingBoxes { get; set; }
    
    [NotMapped]
    public bool IsValidationFailed { get; set; }
    
    [NotMapped]
    public string? ValidationFailureReason { get; set; }
}
