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
///
/// A bank reference alone is NOT an identity. Verified against a real Banco
/// Pichincha statement: one interbank transfer produces three movements — the
/// transfer, the commission and the tax on the commission — all sharing a single
/// "Nro. Documento". Keying on the reference alone collapsed 54 movements into 34.
/// The amount and the direction are therefore part of the key even in reference
/// mode; the date is not, because that is what lets an email notification and the
/// statement row of the same movement still meet.
/// </summary>
public static class TransactionFingerprint
{
    /// <summary>
    /// Bumped whenever the recipe changes, so stored values stay interpretable.
    /// v3 (Entregable 27): the "HEU" day component now uses the account holder's
    /// calendar day (fixed UTC-5) instead of the raw UTC one -- see
    /// <see cref="EcuadorCalendarDay"/>.
    /// </summary>
    public const string Version = "v3";

    /// <summary>Ecuador has no daylight-saving time: a fixed offset is exact, not an approximation.</summary>
    private static readonly TimeSpan EcuadorOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Entregable 27 ("Dedup correo/importación"): the account holder's calendar
    /// day, not the UTC one. An evening purchase in Ecuador (UTC-5) rolls into the
    /// next UTC day -- 23:12 local on 4 March is 04:12 UTC on 5 March -- so keying
    /// identity, or a dedup date-window comparison, on
    /// <see cref="DateTimeOffset.UtcDateTime"/> directly can put one real
    /// movement's email row and its statement row on two different day-keys, or
    /// push a date-window boundary case just far enough to miss it. This mirrors
    /// why <c>StatementDateInterpreter</c> exists on the import side; dedup and the
    /// fingerprint hadn't been updated to match. A real per-user timezone
    /// (<c>User.TimeZoneId</c> already exists) is deliberately not threaded through
    /// here: this is a pure, dependency-free Domain type, and every caller of this
    /// method and of DeduplicationMatcher would need a timezone parameter for a
    /// difference that, while the product is Ecuador-only, is always exactly
    /// UTC-5. Revisit if Nexo ever supports another country.
    /// </summary>
    public static DateOnly EcuadorCalendarDay(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(EcuadorOffset).DateTime);

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
                externalReference!.Trim().ToUpperInvariant(),
                direction.ToString().ToUpperInvariant(),
                Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture))
            : string.Join(
                '|',
                Version,
                "HEU",
                providerCode.ToUpperInvariant(),
                financialAccountId.ToString("N"),
                EcuadorCalendarDay(transactionDate).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
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
