using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nexo.Domain.Transactions;

/// <summary>
/// Deterministic identity of a movement, independent of the channel it arrived on.
/// Two fingerprints being equal is treated as proof of the same movement, so the
/// inputs are deliberately conservative: the calendar day (not the timestamp,
/// which differs between an email notification and the statement row) and the
/// matching-normalized description.
/// </summary>
public static class TransactionFingerprint
{
    /// <summary>Bumped whenever the recipe changes, so stored values stay interpretable.</summary>
    public const string Version = "v1";

    public static string Compute(
        string providerCode,
        Guid financialAccountId,
        string? externalReference,
        DateTimeOffset transactionDate,
        decimal amount,
        TransactionDirection direction,
        string? description)
    {
        var payload = HasUsableReference(externalReference)
            ? string.Join(
                '|',
                Version,
                "REF",
                providerCode.ToUpperInvariant(),
                financialAccountId.ToString("N"),
                externalReference!.Trim().ToUpperInvariant())
            : string.Join(
                '|',
                Version,
                "HEU",
                providerCode.ToUpperInvariant(),
                financialAccountId.ToString("N"),
                transactionDate.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                direction.ToString().ToUpperInvariant(),
                Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture),
                TextNormalizer.NormalizeForMatching(description));

        return Hash(payload);
    }

    /// <summary>
    /// A reference is only usable as a strong key when it actually identifies the
    /// movement. Banks frequently emit "0", "-" or an empty column.
    /// </summary>
    public static bool HasUsableReference(string? externalReference)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return false;
        }

        var trimmed = externalReference.Trim();
        if (trimmed.Length < 4)
        {
            return false;
        }

        return trimmed.Any(char.IsAsciiLetterOrDigit) && trimmed.Trim('0', '-', '.', ' ').Length > 0;
    }

    private static string Hash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(bytes);
    }
}
