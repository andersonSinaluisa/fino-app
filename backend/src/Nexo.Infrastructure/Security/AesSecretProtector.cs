using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;

namespace Nexo.Infrastructure.Security;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "Nexo:Secrets";

    /// <summary>Base64-encoded 32-byte key. Comes from the environment or a KMS, never from source.</summary>
    public string EncryptionKey { get; set; } = string.Empty;
}

/// <summary>
/// AES-256-GCM envelope for third-party secrets (OAuth refresh tokens).
/// Format: <c>v1.nonceBase64.cipherBase64.tagBase64</c>. Authenticated encryption
/// means a tampered value fails loudly instead of decrypting to garbage.
/// </summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Nexo:Secrets:EncryptionKey is required. Generate one with: openssl rand -base64 32");
        }

        _key = Convert.FromBase64String(configured);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Nexo:Secrets:EncryptionKey must decode to exactly 32 bytes.");
        }
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var source = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[source.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, source, cipher, tag);

        return string.Join(
            '.',
            Version,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cipher),
            Convert.ToBase64String(tag));
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);

        var parts = protectedValue.Split('.');
        if (parts.Length != 4 || parts[0] != Version)
        {
            throw new CryptographicException("The protected value has an unexpected format.");
        }

        var nonce = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var tag = Convert.FromBase64String(parts[3]);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>
/// Keyed hash so the audit log can answer "same source?" without ever storing an
/// address. The key makes the hash non-reversible in practice: without it, the
/// IPv4 space is small enough to brute-force.
/// </summary>
public sealed class HmacIpHasher : IIpHasher
{
    private readonly byte[] _key;

    public HmacIpHasher(IOptions<SecretProtectionOptions> options)
    {
        // No fallback salt: a constant that lives in the repository would make the
        // whole IPv4 space brute-forceable, which is exactly what hashing is meant
        // to prevent. Missing configuration is a startup failure, not a downgrade.
        if (string.IsNullOrWhiteSpace(options.Value.EncryptionKey))
        {
            throw new InvalidOperationException(
                "Nexo:Secrets:EncryptionKey is required to hash audit-log addresses.");
        }

        _key = Convert.FromBase64String(options.Value.EncryptionKey);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Nexo:Secrets:EncryptionKey must decode to exactly 32 bytes.");
        }
    }

    public string? Hash(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var bytes = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ipAddress.Trim()));
        return Convert.ToHexStringLower(bytes)[..32];
    }
}
