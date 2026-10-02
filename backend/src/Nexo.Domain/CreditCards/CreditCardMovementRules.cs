using System.Text.RegularExpressions;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Domain.CreditCards;

/// <summary>
/// "Compra en cuotas n de m" leída de la descripción de un estado de tarjeta.
/// </summary>
public readonly record struct InstallmentMarker(int Number, int Count);

/// <summary>
/// LA regla de qué significa cada tipo de movimiento de tarjeta. Todo lo demás
/// (estadísticas, presupuestos, Comprometido, el cliente) lee esto en lugar de
/// repetir un <c>switch</c> propio:
/// <list type="bullet">
/// <item><b>Gasto</b>: Purchase, Interest y Fee. Una compra con tarjeta ES un gasto
/// el día de la compra, aunque el dinero salga del banco un mes después.</item>
/// <item><b>Devolución</b>: Refund resta del gasto de su categoría. Nunca es un ingreso.</item>
/// <item><b>Neutros</b>: Payment, CashAdvance y Adjustment mueven la deuda pero no son
/// ni ingreso ni gasto. Pagar la tarjeta es mover dinero propio (banco → tarjeta);
/// contarlo como gasto sumaría dos veces la misma compra.</item>
/// </list>
/// </summary>
public static partial class CreditCardMovementRules
{
    public const int MaximumInstallments = 72;

    /// <summary>The direction a type demands; null when either is valid (Adjustment).</summary>
    public static TransactionDirection? RequiredDirection(CreditCardMovementType type) => type switch
    {
        CreditCardMovementType.Purchase => TransactionDirection.Expense,
        CreditCardMovementType.Interest => TransactionDirection.Expense,
        CreditCardMovementType.Fee => TransactionDirection.Expense,
        CreditCardMovementType.CashAdvance => TransactionDirection.Expense,
        CreditCardMovementType.Refund => TransactionDirection.Income,
        CreditCardMovementType.Payment => TransactionDirection.Income,
        _ => null,
    };

    /// <summary>
    /// Neutral movements change the debt but are neither income nor spending. They are
    /// excluded from every income/expense figure through the same flag internal
    /// transfers already use (<see cref="Transaction.IsInternalTransfer"/>), so the
    /// existing exclusion in Home, Estadísticas, presupuestos, insights and pulsos
    /// covers them without a second rule to keep in sync.
    /// </summary>
    public static bool IsNeutral(CreditCardMovementType type) =>
        type is CreditCardMovementType.Payment or CreditCardMovementType.CashAdvance or CreditCardMovementType.Adjustment;

    /// <summary>Counts as spending (gasto) in statistics and budgets.</summary>
    public static bool IsSpending(CreditCardMovementType type) =>
        type is CreditCardMovementType.Purchase or CreditCardMovementType.Interest or CreditCardMovementType.Fee;

    /// <summary>Financial cost rather than consumption: shown apart, never as a normal purchase.</summary>
    public static bool IsFinancialCharge(CreditCardMovementType type) =>
        type is CreditCardMovementType.Interest or CreditCardMovementType.Fee;

    /// <summary>A neutral or credit movement cannot be divided into spending categories.</summary>
    public static bool CanBeSplit(CreditCardMovementType? type) =>
        type is null or CreditCardMovementType.Purchase or CreditCardMovementType.Refund
            or CreditCardMovementType.Interest or CreditCardMovementType.Fee;

    public static void EnsureConsistent(CreditCardMovementType type, TransactionDirection direction)
    {
        if (RequiredDirection(type) is { } required && required != direction)
        {
            throw new DomainException(
                "card_movement_direction",
                required == TransactionDirection.Expense
                    ? $"{Label(type)} aumenta la deuda de la tarjeta: debe registrarse como un cargo."
                    : $"{Label(type)} reduce la deuda de la tarjeta: debe registrarse como un abono.");
        }
    }

    public static string Label(CreditCardMovementType type) => type switch
    {
        CreditCardMovementType.Purchase => "Una compra",
        CreditCardMovementType.Refund => "Una devolución",
        CreditCardMovementType.Payment => "Un pago",
        CreditCardMovementType.Interest => "Un interés",
        CreditCardMovementType.Fee => "Una comisión",
        CreditCardMovementType.CashAdvance => "Un avance en efectivo",
        _ => "Un ajuste",
    };

    /// <summary>
    /// What a card movement most likely is, from its direction and the bank's text.
    /// Deterministic and conservative:
    /// <list type="bullet">
    /// <item>A charge is a purchase unless the text clearly says interest, a fee/tax
    /// or a cash advance.</item>
    /// <item>A credit is a payment when the text says so ("PAGO", "ABONO",
    /// "GRACIAS"); any other credit on a card is a merchant giving money back
    /// (devolución) -- a statement never pays you out of nowhere.</item>
    /// </list>
    /// The person can always reclassify; this only decides the default.
    /// </summary>
    public static CreditCardMovementType Classify(TransactionDirection direction, string? description)
    {
        var text = " " + TextNormalizer.Normalize(description) + " ";

        if (direction == TransactionDirection.Income)
        {
            if (ContainsAny(text, RefundWords))
            {
                return CreditCardMovementType.Refund;
            }

            return ContainsAny(text, PaymentWords) ? CreditCardMovementType.Payment : CreditCardMovementType.Refund;
        }

        if (ContainsAny(text, InterestWords))
        {
            return CreditCardMovementType.Interest;
        }

        if (ContainsAny(text, FeeWords))
        {
            return CreditCardMovementType.Fee;
        }

        if (ContainsAny(text, CashAdvanceWords))
        {
            return CreditCardMovementType.CashAdvance;
        }

        return CreditCardMovementType.Purchase;
    }

    /// <summary>
    /// "CUOTA 4/12", "4/12 CUOTAS", "DIFERIDO 04/12", "CUOTA 4 DE 12". A bare "10/09"
    /// is never read as installments -- that is a date.
    /// </summary>
    public static InstallmentMarker? ParseInstallmentMarker(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = description.ToUpperInvariant();
        foreach (var regex in new[] { CuotaFirst(), CuotaAfter(), DiferidoFirst() })
        {
            var match = regex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var number = int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var count = int.Parse(match.Groups["m"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (count >= 2 && count <= MaximumInstallments && number >= 1 && number <= count)
            {
                return new InstallmentMarker(number, count);
            }
        }

        return null;
    }

    /// <summary>
    /// Does a BANK movement read like a payment to a card ("PAGO TARJETA VISA",
    /// "PAGO TC 4582")? Only ever used to SUGGEST a link -- never to link by itself.
    /// </summary>
    public static bool LooksLikeCardPayment(string? description, string? lastFour, CardNetwork? network)
    {
        var text = " " + TextNormalizer.Normalize(description) + " ";
        if (ContainsAny(text, CardPaymentPhrases))
        {
            return true;
        }

        if (!text.Contains(" PAGO ", StringComparison.Ordinal) && !text.Contains(" PAGOS ", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(lastFour) && text.Contains(lastFour, StringComparison.Ordinal))
        {
            return true;
        }

        return network is { } n && NetworkWords(n).Any(w => text.Contains(" " + w + " ", StringComparison.Ordinal));
    }

    private static string[] NetworkWords(CardNetwork network) => network switch
    {
        CardNetwork.Visa => ["VISA"],
        CardNetwork.Mastercard => ["MASTERCARD", "MASTER", "MC"],
        CardNetwork.AmericanExpress => ["AMEX", "AMERICAN EXPRESS"],
        CardNetwork.Diners => ["DINERS"],
        CardNetwork.Discover => ["DISCOVER"],
        _ => [],
    };

    private static readonly string[] CardPaymentPhrases =
    [
        "PAGO TARJETA", "PAGO DE TARJETA", "PAGO TARJETA DE CREDITO", "PAGO TC", "PAGO T C", "PAGO TARJ",
        "PAGO VISA", "PAGO MASTERCARD", "PAGO DINERS", "PAGO AMEX", "PAGO DISCOVER",
    ];

    private static bool ContainsAny(string paddedText, IEnumerable<string> words) =>
        words.Any(w => paddedText.Contains(" " + w + " ", StringComparison.Ordinal));

    private static readonly string[] PaymentWords =
    [
        "PAGO", "PAGOS", "SU PAGO", "ABONO", "ABONOS", "GRACIAS", "PAGO RECIBIDO", "DEBITO AUTOMATICO",
    ];

    private static readonly string[] RefundWords =
    [
        "DEVOLUCION", "DEVOLUCIONES", "REVERSO", "REVERSION", "REEMBOLSO", "ANULACION", "NOTA DE CREDITO",
        "CONTRACARGO", "CASHBACK",
    ];

    private static readonly string[] InterestWords =
    [
        "INTERES", "INTERESES", "INT FINANCIAMIENTO", "INTERES DE MORA", "MORA", "INTERES ROTATIVO",
    ];

    private static readonly string[] FeeWords =
    [
        "COMISION", "COMISIONES", "CARGO ADMINISTRATIVO", "CARGO POR", "MEMBRESIA", "CUOTA ANUAL",
        "DESGRAVAMEN", "IMPUESTO", "ISD", "SOLCA", "IVA COMISION", "EMISION TARJETA", "RENOVACION TARJETA",
    ];

    private static readonly string[] CashAdvanceWords =
    [
        "AVANCE", "AVANCE EFECTIVO", "AVANCE EN EFECTIVO", "RETIRO", "RETIRO CAJERO", "CAJERO", "ATM",
    ];

    [GeneratedRegex(@"CUOTAS?\s*(?<n>\d{1,2})\s*(?:/|DE)\s*(?<m>\d{1,2})\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex CuotaFirst();

    [GeneratedRegex(@"\b(?<n>\d{1,2})\s*/\s*(?<m>\d{1,2})\s*(?:CUOTAS?|CTAS?|DIF)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex CuotaAfter();

    [GeneratedRegex(@"DIF(?:ERIDO|ERIDA)?S?\s*(?<n>\d{1,2})\s*/\s*(?<m>\d{1,2})\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex DiferidoFirst();
}
