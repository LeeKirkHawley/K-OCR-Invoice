using K_OCR_API.Identity;

namespace K_OCR_API.Services;

public interface IJwtTokenService
{
    JwtTokenResult GenerateToken(ApplicationUser user, IEnumerable<string> roles);
}
