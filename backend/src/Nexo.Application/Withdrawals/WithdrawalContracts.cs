namespace Nexo.Application.Withdrawals;

/// <summary>
/// §4 y §9: un movimiento bancario que parece un retiro de efectivo, con todo lo que
/// la tarjeta de conciliación necesita para preguntar sin volver a consultar nada.
/// </summary>
/// <param name="Confidence">"High" sugiere con fuerza; "Medium" pregunta de forma neutra (§3).</param>
/// <param name="MatchedTerm">Qué palabra lo delató, para poder explicar la sugerencia.</param>
/// <param name="SuggestedCashTransactionId">
/// §12: el ingreso de efectivo que la persona ya había registrado a mano y que
/// probablemente ES este retiro. Null cuando no hay ninguno -- entonces Fino creará
/// la pata que falta -- y también cuando hay VARIOS candidatos, porque §13 prohíbe
/// conciliar automáticamente si hay ambigüedad.
/// </param>
/// <param name="AmbiguousMatchCount">
/// Cuántos ingresos de efectivo encajan. 0 o 1 es el caso normal; 2 o más significa
/// que Fino encontró coincidencias pero no puede elegir por su cuenta.
/// </param>
/// <param name="CanLearnPattern">
/// §14/§19: si este patrón se puede guardar como regla. False cuando el texto del
/// banco es demasiado genérico para anclar una regla -- un "RETIRO" pelado se
/// aplicaría a cualquier movimiento que diga "retiro" en cualquier banco.
/// </param>
/// <param name="TimesConfirmedBefore">
/// §19: cuántas veces la persona ya confirmó este mismo patrón en este mismo banco.
/// A partir de cierto número, el cliente ofrece activar la conciliación automática.
/// </param>
public sealed record WithdrawalCandidateDto(
    Guid TransactionId,
    Guid FinancialAccountId,
    string AccountAlias,
    string ProviderCode,
    DateTimeOffset TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    string Confidence,
    string? MatchedTerm,
    Guid? SuggestedCashTransactionId,
    DateTimeOffset? SuggestedCashDate,
    int AmbiguousMatchCount,
    bool CanLearnPattern,
    int TimesConfirmedBefore);

/// <summary>
/// §5 y §10: "sí, pasó a Efectivo".
/// </summary>
/// <param name="CashTransactionId">
/// §12: el ingreso de efectivo ya registrado que corresponde a este retiro. Cuando
/// va null, Fino CREA la pata que falta -- que es el caso normal, porque casi nadie
/// apunta el efectivo antes de que llegue el extracto.
/// </param>
/// <param name="RememberPattern">
/// §19: guardar una regla para este banco y este patrón. Nunca se activa sola: la
/// persona tiene que pedirlo.
/// </param>
public sealed record ConfirmWithdrawalRequest(
    Guid? CashTransactionId = null,
    bool RememberPattern = false);

/// <summary>El resultado de conciliar, para que el cliente pueda decir "Listo. Movimos $100 a Efectivo" (§26).</summary>
/// <param name="CashAccountCreated">True si Fino tuvo que crear la cuenta Efectivo en el momento (§11).</param>
/// <param name="MatchedExistingCashMovement">True si se usó un ingreso que la persona ya tenía registrado (§12).</param>
/// <param name="PatternRemembered">True si se guardó la regla aprendida que pidió la persona (§19).</param>
public sealed record WithdrawalConfirmationDto(
    Guid BankTransactionId,
    Guid CashTransactionId,
    Guid CashAccountId,
    string CashAccountAlias,
    decimal Amount,
    string Currency,
    bool CashAccountCreated,
    bool MatchedExistingCashMovement,
    bool PatternRemembered);

/// <summary>
/// §8: lo que la pantalla de importación necesita para decir "Encontramos 3 posibles
/// retiros" sin detener la importación.
/// </summary>
public sealed record WithdrawalScanDto(int CandidateCount, int HighConfidenceCount);
