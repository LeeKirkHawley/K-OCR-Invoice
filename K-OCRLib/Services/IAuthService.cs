using K_OCR.Models.Api.Auth;

namespace K_OCR.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task SetPasswordAsync(string userId, string encodedToken, string newPassword);
    /// <summary>
    /// Generates a base64url-encoded password reset link for <paramref name="email"/>.
    /// Returns <c>null</c> if no account with that email exists (caller should not reveal this).
    /// </summary>
    Task<string?> GeneratePasswordResetLinkAsync(string email, string baseUrl);
}
