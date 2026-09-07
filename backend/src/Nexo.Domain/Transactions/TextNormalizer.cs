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

    /// <summary>
    /// Personal categorisation rules ("categorización personal"): words that, on
    /// their own, describe almost any movement rather than one merchant. Never a
    /// safe key for a user rule ("COMPRA" -> Transporte would silently swallow
    /// every purchase from every merchant) even though they are legitimate parts
    /// of a raw bank description. Kept separate from <see cref="NoiseTokens"/>,
    /// which is about comparison, not rule safety -- a token can be worth keeping
    /// for matching purposes while still being too generic to anchor a rule alone.
    /// </summary>
    private static readonly HashSet<string> TooGenericRuleWords = new(StringComparer.Ordinal)
    {
        "COMPRA", "COMPRAS", "PAGO", "PAGOS", "CONSUMO", "CONSUMOS", "DEBITO", "CREDITO",
        "TRANSFERENCIA", "TRANSFERENCIAS", "DEPOSITO", "DEPOSITOS", "RETIRO", "RETIROS",
        "ENVIO", "ENVIOS", "RECIBIDO", "COBRO", "COBROS", "VARIOS", "GENERAL", "SERVICIO",
        "SERVICIOS", "OTRO", "OTROS", "MOVIMIENTO", "MOVIMIENTOS", "TARJETA", "CUENTA",
    };

    /// <summary>Minimum length (after normalization) a rule's own match text must have.</summary>
    public const int MinimumRulePatternLength = 3;

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

            // Only treat a standalone numeric token as noise (a masked card suffix,
            // an authorization or reference code) when it is long enough to plausibly
            // be one -- real examples in this codebase and in the "categorización
            // personal" spec are 4-6 digits ("****4821", "829173", "84931"). A short
            // number can be part of a merchant's actual name ("FARMACIA 911" is a real
            // pharmacy chain), so stripping it would destroy a legitimate distinguishing
            // signal instead of removing noise -- see TextNormalizer.SuggestRulePattern.
            if (token.Length >= 4 && token.All(char.IsAsciiDigit))
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
    /// "Categorización personal": the default match text offered when a user turns a
    /// manual category correction into a rule -- the first meaningful token of the
    /// normalized description, e.g. "UBER *TRIP 829173" / "UBER TRIP HELP.UBER.COM" /
    /// "UBER *TRIP 923821" all suggest "UBER", one rule instead of three. Deliberately
    /// just the first token, not <see cref="ExtractMerchant"/>'s first three: a single
    /// stable token is what lets "UBER" (Transporte) and "UBER EATS" (Comida) exist as
    /// two independently useful rules, the more specific one winning on conflict
    /// (see CategorizationEngine).
    /// </summary>
    public static string SuggestRulePattern(string? description) => SuggestRulePattern(description, tokenCount: 1);

    /// <summary>
    /// Point 12 ("conflictos entre reglas"): <paramref name="tokenCount"/> lets a
    /// caller ask for a more specific pattern than the default single token --
    /// "UBER EATS" (2 tokens) instead of "UBER" (1) -- when the single token would
    /// collide with an unrelated existing rule for a different category. See
    /// CategorizationRuleService.ResolveRulePatternAsync, the only caller that
    /// escalates like this; everywhere else keeps the 1-token default.
    /// </summary>
    public static string SuggestRulePattern(string? description, int tokenCount)
    {
        var normalized = NormalizeForMatching(description);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var take = Math.Clamp(tokenCount, 1, tokens.Length);
        return string.Join(' ', tokens.Take(take));
    }

    /// <summary>
    /// Point 18 ("matching seguro"): a rule pattern with too little information is
    /// dangerous -- "COMPRA" or "A" would silently swallow almost every movement
    /// under one category. Checked server-side (never trust mobile validation alone)
    /// before a personal rule pattern is ever persisted, on both the description
    /// pattern and the merchant pattern.
    /// </summary>
    public static bool IsTooGenericRulePattern(string? normalizedPattern)
    {
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return true;
        }

        if (normalizedPattern.Length < MinimumRulePatternLength)
        {
            return true;
        }

        // A pattern made up entirely of generic banking words (e.g. a fallback like
        // "TRANSFERENCIA RECIBIDO" that never reduced to a real merchant token)
        // carries no real merchant signal even though it clears the length bar.
        var tokens = normalizedPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(TooGenericRuleWords.Contains);
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
