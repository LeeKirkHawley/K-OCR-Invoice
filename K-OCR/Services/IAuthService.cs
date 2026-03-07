using K_OCR.Models.Api.Auth;

namespace K_OCR.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task SetPasswordAsync(string userId, string encodedToken, string newPassword);
}
