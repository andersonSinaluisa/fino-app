using System.Text.RegularExpressions;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;

namespace Nexo.Application.EmailIngestion;

/// <summary>
/// Shared machinery for Ecuadorian bank notification emails, which all follow the
/// same Spanish template family ("Estimado cliente, se realizó una compra por
/// USD 48,20 en SUPERMAXI con su tarjeta terminada en 4821").
///
/// A subclass declares its signatures and, when its wording differs, overrides a
/// pattern set. The patterns here are written against publicly visible notification
/// wording and the fixtures in tests/; they must be validated against real samples
/// before phase 2 ships (see docs/email-ingestion.md).
/// </summary>
public abstract partial class SpanishNotificationEmailParser : IBankEmailParser
{
    public abstract string ProviderCode { get; }

    public abstract string ParserCode { get; }

    public virtual int Priority => 100;

    /// <summary>Words that must appear for this parser to claim the message.</summary>
    protected abstract IReadOnlyList<string> Signatures { get; }

    protected virtual IReadOnlyList<string> ExpenseKeywords =>
    [
        "COMPRA", "CONSUMO", "DEBITO", "DEBITAR", "PAGO", "RETIRO",
        "TRANSFERENCIA ENVIADA", "ENVIASTE", "ENVIO DE DINERO", "DEBITO REALIZADO",
    ];

    protected virtual IReadOnlyList<string> IncomeKeywords =>
    [
        "ACREDITACION", "ACREDITADO", "DEPOSITO", "TRANSFERENCIA RECIBIDA",
        "RECIBISTE", "ABONO", "CREDITO A SU CUENTA", "PAGO RECIBIDO",
    ];

    /// <summary>Marketing and security emails that must never become movements.</summary>
    protected virtual IReadOnlyList<string> ExcludeKeywords =>
    [
        "ESTADO DE CUENTA DISPONIBLE", "CAMBIO DE CLAVE", "PROMOCION", "PROMOCIONES",
        "ENCUESTA", "BOLETIN", "NEWSLETTER", "TERMINOS Y CONDICIONES", "INTENTO DE ACCESO",

        // Entregable 26 ("Parsers de correo"): a payment-due reminder or a
        // buy-now-pay-later promo says "pago" next to a dollar figure without
        // describing anything that already happened -- exactly what the bare
        // "PAGO" in ExpenseKeywords (below) would otherwise misread as an expense.
        "FECHA DE PAGO", "FECHA LIMITE DE PAGO", "PAGO MINIMO", "MONTO A PAGAR",
        "RECORDATORIO DE PAGO", "PROXIMO A VENCER", "PENDIENTE DE PAGO",
    ];

    public virtual bool CanParse(EmailMessage message)
    {
        var text = TextNormalizer.Normalize(message.SearchableText);
        if (text.Length == 0)
        {
            return false;
        }

        // Entregable 26: normalize the keyword too, the same way Signatures (below)
        // and DirectionOf already do -- text is accent-stripped/upper-cased, so a
        // literal "PROMOCIÓN" here would otherwise never match anything again.
        // Today's list happens to already be accent-free by coincidence; this stops
        // that from being a silent trap the next time someone adds one naturally.
        if (ExcludeKeywords.Any(k => text.Contains(TextNormalizer.Normalize(k), StringComparison.Ordinal)))
        {
            return false;
        }

        if (Signatures.Count > 0 && !Signatures.Any(s => text.Contains(TextNormalizer.Normalize(s), StringComparison.Ordinal)))
        {
            return false;
        }

        return DirectionOf(text) is not null && AmountRegex().IsMatch(message.SearchableText);
    }

    public virtual Task<ParsedTransaction?> ParseAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var raw = message.SearchableText;
        var normalized = TextNormalizer.Normalize(raw);

        var direction = DirectionOf(normalized);
        if (direction is null)
        {
            return Task.FromResult<ParsedTransaction?>(null);
        }

        if (!TryReadAmount(raw, out var amount))
        {
            return Task.FromResult<ParsedTransaction?>(null);
        }

        var merchant = ReadMerchant(raw);
        var mask = ReadMask(raw);
        var reference = ReadReference(raw);
        var occurredAt = ReadDate(raw) ?? message.ReceivedAt;

        var description = BuildDescription(message, merchant, direction.Value);

        return Task.FromResult<ParsedTransaction?>(new ParsedTransaction(
            ProviderCode,
            amount,
            direction.Value,
            occurredAt,
            description,
            merchant,
            mask,
            reference,
            // An email is a notification, not a posted statement line: the final
            // amount can still change (tips, holds), so confidence is never High.
            SourceConfidence.Medium));
    }

    protected TransactionDirection? DirectionOf(string normalizedText)
    {
        var income = IncomeKeywords.Any(k => normalizedText.Contains(TextNormalizer.Normalize(k), StringComparison.Ordinal));
        var expense = ExpenseKeywords.Any(k => normalizedText.Contains(TextNormalizer.Normalize(k), StringComparison.Ordinal));

        if (income && !expense)
        {
            return TransactionDirection.Income;
        }

        if (expense && !income)
        {
            return TransactionDirection.Expense;
        }

        if (income && expense)
        {
            // "Transferencia recibida" also contains "recibida" and sometimes "pago";
            // the more specific income phrasing wins.
            return TransactionDirection.Income;
        }

        return null;
    }

    /// <summary>
    /// Entregable 26 ("Parsers de correo"): "Su saldo disponible es de USD 1.204,33.
    /// Se realizó una compra por USD 48,20 en..." is a completely ordinary real
    /// notification shape, and the old version of this method took whichever dollar
    /// figure came first -- the balance, not the movement. An amount is skipped when
    /// the text right before it looks like a balance/limit rather than a movement;
    /// the first one that does not is trusted, same as before when there is only one.
    /// </summary>
    protected virtual bool TryReadAmount(string raw, out decimal amount)
    {
        amount = 0m;

        foreach (Match match in AmountRegex().Matches(raw))
        {
            if (!AmountParser.TryParse(match.Groups["amount"].Value, out var parsed) || parsed <= 0m)
            {
                continue;
            }

            var windowStart = Math.Max(0, match.Index - AmountContextWindow);
            var preceding = TextNormalizer.Normalize(raw[windowStart..match.Index]);
            if (BalanceContextKeywords.Any(term => preceding.Contains(term, StringComparison.Ordinal)))
            {
                continue;
            }

            amount = parsed;
            return true;
        }

        return false;
    }

    private const int AmountContextWindow = 40;

    private static readonly string[] BalanceContextKeywords = ["SALDO", "CUPO", "DISPONIBLE", "LIMITE"];

    /// <summary>
    /// Walks the candidates and takes the first that actually looks like a merchant.
    /// "...en su cuenta terminada en 4821" matches the pattern but is not a name, so
    /// a candidate that reduces to nothing (or to digits) is skipped rather than
    /// becoming the movement's description.
    /// </summary>
    protected virtual string? ReadMerchant(string raw)
    {
        foreach (Match match in MerchantRegex().Matches(raw))
        {
            var value = match.Groups["merchant"].Value.Trim(' ', '.', ',', ';', ':', '-');
            if (value.Length < 3 || !value.Any(char.IsLetter))
            {
                continue;
            }

            var merchant = TextNormalizer.ExtractMerchant(value);
            if (!string.IsNullOrWhiteSpace(merchant) && merchant.Length >= 3 && merchant.Any(char.IsLetter))
            {
                return merchant;
            }
        }

        return null;
    }

    protected virtual string? ReadMask(string raw)
    {
        var match = MaskRegex().Match(raw);
        return match.Success ? match.Groups["mask"].Value : null;
    }

    protected virtual string? ReadReference(string raw)
    {
        var match = ReferenceRegex().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["reference"].Value.Trim();
        return TransactionFingerprint.HasUsableReference(value) ? value : null;
    }

    protected virtual DateTimeOffset? ReadDate(string raw)
    {
        var match = DateRegex().Match(raw);
        if (!match.Success || !DateParser.TryParseDate(match.Groups["date"].Value, out var date))
        {
            return null;
        }

        var time = TimeSpan.FromHours(12);
        var timeMatch = TimeRegex().Match(raw);
        if (timeMatch.Success
            && int.TryParse(timeMatch.Groups["h"].Value, out var hour)
            && int.TryParse(timeMatch.Groups["m"].Value, out var minute)
            && hour is >= 0 and < 24
            && minute is >= 0 and < 60)
        {
            // Entregable 26: "a las 6:12 PM" is a 12-hour clock -- without this,
            // the meridiem was matched by nothing and silently thrown away, so a
            // real PM time was stored as if it were AM (off by up to 12 hours).
            if (timeMatch.Groups["ampm"].Success && hour is >= 1 and <= 12)
            {
                var isPm = timeMatch.Groups["ampm"].Value.Contains('P', StringComparison.OrdinalIgnoreCase);
                hour = isPm
                    ? (hour == 12 ? 12 : hour + 12)
                    : (hour == 12 ? 0 : hour);
            }

            time = new TimeSpan(hour, minute, 0);
        }

        return StatementDateInterpreter.Ecuador().ToInstant(date, time);
    }

    protected virtual string BuildDescription(EmailMessage message, string? merchant, TransactionDirection direction)
    {
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            return merchant;
        }

        var subject = message.Subject.Trim();
        if (subject.Length > 0)
        {
            return subject.Length > 120 ? subject[..120] : subject;
        }

        return direction == TransactionDirection.Income ? "Ingreso" : "Gasto";
    }

    [GeneratedRegex(@"(?:USD|US\$|\$)\s*(?<amount>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?|\d+(?:[.,]\d{2})?)", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex AmountRegex();

    // Lazy, and stopped by the words that follow a merchant name in these
    // templates. A greedy match on a single-line body captures "TIENDA X con su
    // tarjeta terminada en 4821", which then poisons the description and the
    // fingerprint, so the email row stops matching its statement row.
    // Three details that each cost a real bug:
    //   \b after the keyword  -> "Enviaste" must not match the "en" inside it
    //   [:\s]+ (not \s*:?\s*) -> the separator cannot backtrack past the stop-word check
    //   lazy body + lookahead -> "TIENDA X con su tarjeta..." stops at "con"
    [GeneratedRegex(@"\b(?:en|a favor de|comercio|establecimiento)\b[:\s]+(?!(?:su|tu|mi|la|el|los|las|un|una|sus|tus|comercio|establecimiento|cuenta|tarjeta)\b)(?<merchant>[A-Za-z0-9ÁÉÍÓÚÑáéíóúñ&.\-][A-Za-z0-9ÁÉÍÓÚÑáéíóúñ&.\- ]{2,44}?)(?=\s+(?:con|el|por|usando|mediante|a\s+las|desde)\b|[.,;:\n]|$)", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex MerchantRegex();

    [GeneratedRegex(@"(?:terminad[ao]\s+en\s*|final(?:izad[ao])?\s+en\s*|\*{2,}\s*)(?<mask>\d{4})", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex MaskRegex();

    [GeneratedRegex(@"(?:referencia|comprobante|documento|transacci[oó]n\s*(?:n[uú]mero|nro)?|autorizaci[oó]n)\s*[:#]?\s*(?<reference>[A-Za-z0-9\-]{4,24})", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex ReferenceRegex();

    // Entregable 26: the year-first alternative used to require exactly two digits
    // for month and day (\d{2}-\d{2}), so "2026-8-31" (a single-digit month/day --
    // the same shape ValueParsers.cs's own comment says Pichincha's web export
    // actually uses) fell through to the day/month alternative instead, which then
    // matched only the truncated tail "26-8-31" and lost the century. The \b at
    // both ends stops either alternative from ever starting or ending mid-run.
    [GeneratedRegex(@"\b(?<date>\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\b", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex DateRegex();

    // Entregable 26: an optional meridiem group -- "6:12 PM" has no way to reach
    // ReadDate as anything but a plain 18-minus-12-hours-wrong "06:12" without it.
    [GeneratedRegex(@"\b(?<h>\d{1,2}):(?<m>\d{2})(?::\d{2})?\s*(?<ampm>[AaPp]\.?\s?[Mm]\.?)?\b", RegexOptions.IgnoreCase, 500)]
    protected static partial Regex TimeRegex();
}
