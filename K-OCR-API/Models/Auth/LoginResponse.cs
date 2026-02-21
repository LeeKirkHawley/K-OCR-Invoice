namespace K_OCR_API.Models.Auth;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
    public DateTime ExpiresAtUtc { get; set; }
}
