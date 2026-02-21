using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using K_OCR_API.Configuration;
using K_OCR_API.Identity;

namespace K_OCR_API.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly byte[] _key;

    public JwtTokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value ?? throw new InvalidOperationException("Missing JWT settings.");
        var secret = _settings.Secret ?? throw new InvalidOperationException("Jwt:Secret is required.");
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public JwtTokenResult GenerateToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Max(1, _settings.TokenValidityMinutes));

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim("TenantId", user.OrganizationId ?? string.Empty),
            new Claim("SecurityStamp", user.SecurityStamp ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => new Claim(ClaimTypes.Role, r)));

        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: signingCredentials);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new JwtTokenResult(token, expires);
    }
}
