namespace Nexo.Domain.Transactions;

/// <summary>
/// Qué tan seguro está Fino de que un movimiento bancario es un retiro de efectivo.
/// </summary>
public enum WithdrawalConfidence
{
    /// <summary>No lo parece. No se cambia nada ni se pregunta (§3).</summary>
    None = 0,

    /// <summary>Podría serlo. Se pregunta de forma neutra (§3).</summary>
    Medium = 1,

    /// <summary>Casi seguro. Se sugiere con fuerza (§3).</summary>
    High = 2,
}

/// <param name="Confidence">Ver <see cref="WithdrawalConfidence"/>.</param>
/// <param name="MatchedTerm">El término que disparó la detección, para poder explicarla y para aprender de ella (§14).</param>
/// <param name="Score">Puntuación cruda, útil para depurar y para ordenar candidatos.</param>
public readonly record struct WithdrawalSignal(
    WithdrawalConfidence Confidence,
    string? MatchedTerm,
    int Score)
{
    public bool IsCandidate => Confidence != WithdrawalConfidence.None;

    public static WithdrawalSignal NotAWithdrawal => new(WithdrawalConfidence.None, null, 0);
}

/// <summary>
/// §2 y §3: detección de retiros de efectivo a partir de lo que escribe el banco.
///
/// Es una clase de dominio PURA -- sin base de datos, sin servicios -- por dos
/// razones. La primera es que así se puede probar exhaustivamente con las decenas
/// de variantes reales que usan los bancos ecuatorianos. La segunda es que la
/// detección tiene que dar el mismo resultado se llame desde donde se llame: desde
/// la importación, desde la bandeja "Por revisar" o desde el detalle de un
/// movimiento. Una detección que dependiera del contexto sería una detección en la
/// que no se puede confiar.
///
/// LO QUE ESTA CLASE NO DECIDE: no concilia, no crea movimientos y no cambia
/// categorías. Solo responde "¿esto parece un retiro, y cuánto lo parece?".
/// </summary>
public static class WithdrawalDetector
{
    /// <summary>
    /// §15 y §24: "COMISION RETIRO ATM" NO es un retiro, es una comisión -- y es un
    /// error caro, porque convertiría un gasto real en una transferencia interna y
    /// lo sacaría de las estadísticas de gasto.
    ///
    /// Por eso esto se comprueba ANTES que cualquier palabra de retiro, y por eso es
    /// una lista de exclusión absoluta y no una resta de puntos: da igual cuántas
    /// señales de retiro tenga la línea, si dice "COMISION" no es un retiro. El
    /// motor de categorización ya tiene reglas sembradas que mandan estas palabras a
    /// "Comisiones e impuestos"; aquí simplemente no le disputamos ese movimiento.
    /// </summary>
    private static readonly string[] FeeTerms =
    [
        "COMISION", "COMISIONES", "IVA", "RETENCION", "IMPUESTO", "COSTO",
        "CARGO POR", "TARIFA", "FEE", "SERVICIO POR",
    ];

    /// <summary>
    /// Términos que por sí solos identifican un retiro. La lista sale de las
    /// variantes que el spec recogió de extractos reales, ya normalizadas (sin
    /// tildes, en mayúsculas, sin guiones): "RETIRO-ATM 00123" llega aquí como
    /// "RETIRO ATM 00123".
    ///
    /// Se comparan como SUBCADENA sobre el texto normalizado, no como palabra
    /// exacta, porque los bancos pegan códigos a las palabras ("RETINJ0012").
    /// </summary>
    private static readonly string[] StrongTerms =
    [
        "RETIRO", "RETIROS", "RETIR",
        // Abreviaturas que usa Pichincha en algunos extractos. No son palabras, así
        // que sin esta lista no habría forma de reconocerlas.
        "RETINJ", "RETIINJ", "RET NAC",
        "CAJERO", "CAJEROS",
        "CASH WITHDRAWAL", "ATM WITHDRAWAL", "WITHDRAWAL",
        "EFECTIVO ATM",
    ];

    /// <summary>
    /// Señales que apoyan pero no bastan solas. §3 lo dice explícitamente: "no
    /// marcar automáticamente como retiro solo por contener ATM" -- hay comercios
    /// que se llaman ATM y transacciones de red que lo mencionan sin ser retiros.
    /// </summary>
    private static readonly string[] SupportingTerms =
    [
        "ATM", "CAJ AUT", "VENTANILLA", "OFICINA", "RED BANRED", "BANRED",
        "INTERNACIONAL", "NACIONAL", "AUTOMATICO",
    ];

    /// <summary>
    /// §3: "monto redondo frecuente para cajero". Los cajeros ecuatorianos entregan
    /// billetes de 10 y 20, así que un retiro real casi siempre es múltiplo de 10 y
    /// sin centavos. No prueba nada por sí solo -- un gasto de $20 exactos también
    /// lo cumple -- pero suma cuando ya hay otra señal.
    /// </summary>
    private static bool IsAtmShapedAmount(decimal amount) =>
        amount > 0m && amount % 10m == 0m;

    private const int StrongScore = 60;
    private const int SupportingScore = 15;
    private const int RoundAmountScore = 10;
    private const int ExpenseScore = 10;

    /// <summary>Desde aquí se sugiere con fuerza (§3).</summary>
    private const int HighThreshold = 75;

    /// <summary>Desde aquí se pregunta de forma neutra (§3).</summary>
    private const int MediumThreshold = 45;

    /// <summary>
    /// Analiza un movimiento. <paramref name="description"/> es el texto crudo del
    /// banco; se normaliza aquí dentro para que quien llama no tenga que acordarse.
    /// </summary>
    /// <param name="externalReference">
    /// El código de documento del banco cuando lo hay. Algunos extractos ponen la
    /// pista del retiro ahí y no en el concepto.
    /// </param>
    public static WithdrawalSignal Detect(
        string? description,
        TransactionDirection direction,
        decimal amount,
        string? externalReference = null)
    {
        // Un retiro saca dinero de la cuenta. Un ingreso que diga "RETIRO" es otra
        // cosa (la reversa de un retiro, por ejemplo) y no debe proponerse.
        if (direction != TransactionDirection.Expense)
        {
            return WithdrawalSignal.NotAWithdrawal;
        }

        var haystack = TextNormalizer.Normalize(description);

        if (haystack.Length == 0)
        {
            return WithdrawalSignal.NotAWithdrawal;
        }

        var reference = TextNormalizer.Normalize(externalReference);
        if (reference.Length > 0)
        {
            haystack = $"{haystack} {reference}";
        }

        // §24: las reglas más específicas primero. Una comisión nunca es un retiro,
        // por muchas otras señales que tenga.
        if (FeeTerms.Any(term => haystack.Contains(term, StringComparison.Ordinal)))
        {
            return WithdrawalSignal.NotAWithdrawal;
        }

        var strong = StrongTerms.FirstOrDefault(term => haystack.Contains(term, StringComparison.Ordinal));
        var supporting = SupportingTerms
            .Where(term => haystack.Contains(term, StringComparison.Ordinal))
            .ToArray();

        if (strong is null && supporting.Length == 0)
        {
            return WithdrawalSignal.NotAWithdrawal;
        }

        var score = 0;

        if (strong is not null)
        {
            score += StrongScore;
        }

        // Cada señal de apoyo suma, pero se topan: cinco palabras débiles no deben
        // valer más que un "RETIRO" explícito.
        score += Math.Min(supporting.Length, 2) * SupportingScore;

        if (IsAtmShapedAmount(amount))
        {
            score += RoundAmountScore;
        }

        // La dirección ya está garantizada arriba; se puntúa aparte para que el
        // número sea legible al depurar ("¿de dónde salieron estos 80?").
        score += ExpenseScore;

        var confidence = score switch
        {
            >= HighThreshold => WithdrawalConfidence.High,
            >= MediumThreshold => WithdrawalConfidence.Medium,
            _ => WithdrawalConfidence.None,
        };

        // Solo señales de apoyo y nada más: "ATM" suelto se queda en Medium como
        // mucho, nunca en High. §3: "no marcar automáticamente como retiro solo por
        // contener ATM".
        if (strong is null && confidence == WithdrawalConfidence.High)
        {
            confidence = WithdrawalConfidence.Medium;
        }

        if (confidence == WithdrawalConfidence.None)
        {
            return WithdrawalSignal.NotAWithdrawal;
        }

        return new WithdrawalSignal(confidence, strong ?? supporting.FirstOrDefault(), score);
    }

    /// <summary>
    /// §14: el patrón que se guardaría como regla aprendida para este movimiento.
    /// Reutiliza <see cref="TextNormalizer.SuggestRulePattern"/>, el mismo que usa
    /// la categorización personal, para que una regla aprendida de un retiro sea
    /// indistinguible de cualquier otra regla del usuario.
    /// </summary>
    public static string? SuggestPattern(string? description)
    {
        var pattern = TextNormalizer.SuggestRulePattern(description, tokenCount: 2);
        return string.IsNullOrWhiteSpace(pattern) ? null : pattern;
    }
}
