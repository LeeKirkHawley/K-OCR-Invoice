using System.ComponentModel.DataAnnotations;

namespace K_OCR.Models.Api.OrganizationAdmin;

public class InviteUserRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
