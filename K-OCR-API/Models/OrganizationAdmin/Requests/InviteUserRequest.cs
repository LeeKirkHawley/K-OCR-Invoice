using System.ComponentModel.DataAnnotations;

namespace K_OCR_API.Models.OrganizationAdmin.Requests;

public class InviteUserRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
