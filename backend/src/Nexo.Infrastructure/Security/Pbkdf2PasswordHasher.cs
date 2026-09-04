using System.Security.Cryptography;
using Nexo.Application.Abstractions;

namespace Nexo.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 with a per-password salt, in a self-describing format so the
/// iteration count can be raised later without invalidating existing hashes:
/// <c>pbkdf2-sha512$iterations$saltBase64$hashBase64</c>.
///
/// ADR-002: written on the BCL rather than pulled from a package. It is 60 lines,
/// it is the only place a password is touched, and being explicit about the
/// parameters is worth more here than saving those lines.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha512";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>OWASP guidance for PBKDF2-HMAC-SHA512 (2024): 210k iterations.</summary>
    private const int CurrentIterations = 210_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, CurrentIterations);

        return string.Join(
            '$',
            Prefix,
            CurrentIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string hash, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);
        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);

        needsRehash = matches && iterations < CurrentIterations;
        return matches;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, length);
}
