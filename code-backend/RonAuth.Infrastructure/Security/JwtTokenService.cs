using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RonAuth.Application.Abstractions;
using RonAuth.Application.Options;
using RonAuth.Domain.Users;

namespace RonAuth.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtSettings> jwtSettings) : ITokenService
{
    private readonly JwtSecurityTokenHandler tokenHandler = new();

    public string GenerateAccessToken(User user, IReadOnlyList<UserAccess> accesses)
    {
        var currentTime = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
        };

        claims.AddRange(accesses.Select(item => new Claim(ClaimTypes.Role, item.RoleName)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtSettings.Value.Issuer,
            Audience = jwtSettings.Value.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = currentTime.AddMinutes(jwtSettings.Value.AccessTokenLifetimeMinutes),
            SigningCredentials = CreateSigningCredentials(),
        };

        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateTemporaryToken(Guid userId, string providerName, TimeSpan lifetime)
    {
        var currentTime = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtSettings.Value.Issuer,
            Audience = jwtSettings.Value.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("provider", providerName),
                new Claim("token_type", "temporary"),
            ]),
            Expires = currentTime.Add(lifetime),
            SigningCredentials = CreateSigningCredentials(),
        };

        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    public (Guid UserId, string ProviderName)? ValidateTemporaryToken(string token)
    {
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Value.Issuer,
                ValidAudience = jwtSettings.Value.Audience,
                IssuerSigningKey = CreateSecurityKey(),
                ClockSkew = TimeSpan.Zero,
            }, out _);

            if (!string.Equals(principal.FindFirstValue("token_type"), "temporary", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var providerName = principal.FindFirstValue("provider");
            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (providerName is null || !Guid.TryParse(subject, out var userId))
            {
                return null;
            }

            return (userId, providerName);
        }
        catch
        {
            return null;
        }
    }

    private SigningCredentials CreateSigningCredentials()
    {
        return new SigningCredentials(CreateSecurityKey(), SecurityAlgorithms.HmacSha256);
    }

    private SymmetricSecurityKey CreateSecurityKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Value.SigningKey));
    }
}