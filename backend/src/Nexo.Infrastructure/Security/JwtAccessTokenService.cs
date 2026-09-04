using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Nexo:Jwt";

    public string Issuer { get; set; } = "nexo-api";

    public string Audience { get; set; } = "nexo-mobile";

    /// <summary>At least 32 bytes. Supplied through configuration/secrets, never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>
/// Short-lived HS256 access tokens plus opaque, hashed refresh tokens.
/// The access token carries only the user id and display name — never balances,
/// never the email in a claim the client might log.
/// </summary>
public sealed class JwtAccessTokenService(IOptions<JwtOptions> options, IClock clock) : IAccessTokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenResult Issue(User user)
    {
        if (_options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Nexo:Jwt:SigningKey must be at least 32 characters. Set it in configuration or user secrets.");
        }

        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("name", user.DisplayName),
                new Claim("tz", user.TimeZoneId),
            ]),
        };

        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        return new AccessTokenResult(handler.CreateToken(descriptor), expires);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (token, HashRefreshToken(token));
    }

    /// <summary>
    /// Refresh tokens are high-entropy random values, so a plain SHA-256 is the right
    /// tool: no salt is needed and lookups stay a single indexed equality check.
    /// </summary>
    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
