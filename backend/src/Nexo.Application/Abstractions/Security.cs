using Nexo.Domain.Users;

namespace Nexo.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time verification. Returns true and a rehash when parameters are outdated.</summary>
    bool Verify(string password, string hash, out bool needsRehash);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenService
{
    /// <summary>
    /// Entregable 19: <paramref name="sessionId"/> is the RefreshToken row this
    /// access token was issued alongside, carried as the "sid" claim so a
    /// request can know its own session without resending the refresh token
    /// (a credential, and one this API never wants to see outside /auth).
    /// </summary>
    AccessTokenResult Issue(User user, Guid sessionId);

    /// <summary>Generates an opaque refresh token plus the hash that gets persisted.</summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}

/// <summary>
/// Envelope encryption for third-party secrets (OAuth refresh tokens). Values are
/// never stored or logged in clear text; only the reference returned here is.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

/// <summary>Hashes IPs before they touch the audit log, so logs hold no raw addresses.</summary>
public interface IIpHasher
{
    string? Hash(string? ipAddress);
}
