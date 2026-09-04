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
    AccessTokenResult Issue(User user);

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
