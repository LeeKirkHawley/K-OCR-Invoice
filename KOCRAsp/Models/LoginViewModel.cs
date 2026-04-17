using System.ComponentModel.DataAnnotations;
using KOCRAsp.Models.Validation;

namespace KOCRAsp.Models;

public class LoginViewModel
{
    [Required]
    [EmailOrGuestUserName]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
