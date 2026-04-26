using System.ComponentModel.DataAnnotations;

namespace K_OCR.Models;

public class BatchAction
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    [Required, MaxLength(200)]
    public string Organization { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string OrgUser { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BatchName { get; set; } = string.Empty;
}
