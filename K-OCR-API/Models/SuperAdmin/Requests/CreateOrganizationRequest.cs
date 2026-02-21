using System.ComponentModel.DataAnnotations;

namespace K_OCR_API.Models.SuperAdmin.Requests;

public class CreateOrganizationRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;
}
