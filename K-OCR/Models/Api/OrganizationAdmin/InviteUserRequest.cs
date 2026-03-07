using System.ComponentModel.DataAnnotations;

namespace K_OCR.Models.Api.OrganizationAdmin;

public class InviteUserRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Role to assign: OrganizationAdmin, OrganizationValidator, or OrganizationUser.
    /// </summary>
    [Required]
    public string Role { get; set; } = "OrganizationUser";
}
