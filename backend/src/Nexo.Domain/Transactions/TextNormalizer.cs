using System.Globalization;
using System.Text;

namespace Nexo.Domain.Transactions;

/// <summary>
/// Turns a bank description into a stable comparison key.
/// The same purchase is described very differently by an email notification
/// ("Compra en SUPERMAXI ALBORADA con tu Tarjeta ****4821") and by a statement row
/// ("SUPERMAXI ALBORADA GUAYAQUIL"), so both must reduce to the same core tokens
/// before they can be compared.
/// </summary>
public static class TextNormalizer
{
    private static readonly string[] NoiseTokens =
    [
        "COMPRA", "COMPRAS", "PAGO", "PAGOS", "CONSUMO", "CONSUMOS", "DEBITO", "CREDITO",
        "TARJETA", "TARJ", "TC", "TD", "CTA", "CUENTA", "TRANSACCION", "TRX", "REF",
        "REFERENCIA", "EN", "DE", "DEL", "LA", "EL", "CON", "TU", "SU", "POR", "A",
        "ESTABLECIMIENTO", "COMERCIO", "LOCAL", "SUCURSAL", "AUTORIZACION", "AUT",
        "INTERNET", "WEB", "MOVIL", "APP", "PUNTO", "VENTA", "POS", "ECUADOR", "EC",
    ];

    /// <summary>Upper-cased, accent-free, punctuation-free, single-spaced text.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Normalized text with masked card digits, standalone numbers and generic
    /// banking words removed. This is what the fingerprint and the similarity
    /// comparison operate on.
    /// </summary>
    public static string NormalizeForMatching(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length <= 1)
            {
                continue;
            }

            if (token.All(char.IsAsciiDigit))
            {
                continue;
            }

            if (NoiseTokens.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            kept.Add(token);
        }

        return kept.Count == 0 ? normalized : string.Join(' ', kept);
    }

    /// <summary>
    /// Best-effort merchant name: the first few meaningful tokens, title-cased for
    /// display. "SUPERMAXI ALBORADA GUAYAQUIL" becomes "Supermaxi Alborada".
    /// </summary>
    public static string? ExtractMerchant(string? description)
    {
        var normalized = NormalizeForMatching(description);
        if (normalized.Length == 0)
        {
            return null;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        return string.Join(
            ' ',
            tokens.Select(t => t.Length == 1 ? t : t[..1] + t[1..].ToLowerInvariant()));
    }

    /// <summary>
    /// Token-level Jaccard similarity in [0,1]. Chosen over edit distance because
    /// bank descriptions differ by whole words (city, branch, channel), not letters.
    /// </summary>
    public static double Similarity(string? left, string? right)
    {
        var a = NormalizeForMatching(left);
        var b = NormalizeForMatching(right);

        if (a.Length == 0 && b.Length == 0)
        {
            return 1d;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0d;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1d;
        }

        var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;

        return union == 0 ? 0d : (double)intersection / union;
    }
}
