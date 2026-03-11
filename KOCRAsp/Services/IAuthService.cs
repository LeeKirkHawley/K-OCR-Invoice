using KOCRAsp.Models.Api.Auth;

namespace KOCRAsp.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task SetPasswordAsync(string userId, string encodedToken, string newPassword);
}
