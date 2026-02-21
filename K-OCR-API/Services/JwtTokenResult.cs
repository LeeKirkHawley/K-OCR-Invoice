namespace K_OCR_API.Services;

public record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
