using System.ComponentModel.DataAnnotations;

namespace K_OCRLib.Models.Api.SuperAdmin;

public class CreateOrganizationRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    [Required]
    public string AdminName { get; set; } = string.Empty;

    public bool IsBetaTestOrganization { get; set; }
    public int? BetaMaxOcrPages { get; set; }
}
