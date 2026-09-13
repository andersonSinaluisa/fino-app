namespace Nexo.Application.QuickEntry;

/// <summary>
/// Registro rápido de efectivo: lo que el cliente manda para crear un movimiento
/// escrito a mano. Todo salvo <paramref name="Amount"/> es opcional a propósito
/// -- la regla del producto es que "+ → 12.50 → Guardar" ya es un movimiento
/// válido, así que nada aquí puede obligar a categorizar, describir ni elegir
/// fecha.
/// </summary>
/// <param name="Amount">
/// Siempre positivo. El signo lo decide <paramref name="Direction"/>, igual que
/// en <c>Transaction</c>: un monto negativo se rechaza en vez de reinterpretarse,
/// porque adivinar la intención de un "-5" en un campo que ya tiene selector de
/// gasto/ingreso es exactamente el tipo de suposición que produce saldos
/// equivocados.
/// </param>
/// <param name="Direction">"Expense" (por defecto) o "Income".</param>
/// <param name="Description">
/// Vacío o null se convierte en un texto neutro ("Efectivo" / "Ingreso en
/// efectivo"). Nunca se inventa un comercio.
/// </param>
/// <param name="FinancialAccountId">
/// Null significa "la cuenta de efectivo", que se crea sola la primera vez. Se
/// acepta otra cuenta para que el mismo caso de uso sirva al formulario completo
/// ("Más detalles") sin duplicar nada.
/// </param>
/// <param name="CategoryId">
/// Null NO significa "sin categoría": significa "no sé, decide tú", y entonces
/// corre el motor de reglas que ya categoriza importaciones y correos. Para pedir
/// explícitamente "Sin categoría" está <paramref name="LeaveUncategorized"/>.
/// </param>
/// <param name="LeaveUncategorized">
/// True cuando la persona quitó la categoría a mano. Se respeta como una decisión
/// suya: las reglas no vuelven a ponerle una después.
/// </param>
/// <param name="OccurredAt">Null = ahora.</param>
/// <param name="ClientRequestId">
/// §36: identificador generado por el cliente ANTES de enviar. Si llega dos veces
/// (doble toque, reintento tras un timeout) la segunda devuelve el movimiento ya
/// creado en lugar de crear otro.
/// </param>
public sealed record CreateQuickTransactionRequest(
    decimal Amount,
    string? Direction = null,
    string? Description = null,
    Guid? FinancialAccountId = null,
    Guid? CategoryId = null,
    bool LeaveUncategorized = false,
    DateTimeOffset? OccurredAt = null,
    Guid? ClientRequestId = null,
    string? Note = null);

/// <summary>
/// Registro rápido de efectivo (§22, "Más detalles"): corrige un movimiento que
/// la persona escribió a mano. Nunca se aplica a movimientos que vinieron de un
/// banco.
/// </summary>
public sealed record UpdateQuickTransactionRequest(
    decimal Amount,
    string? Direction = null,
    string? Description = null,
    Guid? CategoryId = null,
    bool LeaveUncategorized = false,
    DateTimeOffset? OccurredAt = null);

/// <summary>
/// §24 y §25: el saldo de efectivo declarado por la persona ("¿cuánto efectivo
/// tienes?") y su corrección posterior.
/// </summary>
/// <param name="Balance">
/// El efectivo que de verdad tiene ahora mismo, no la diferencia.
/// </param>
/// <param name="Mode">
/// "Anchor" ancla el saldo sin tocar el historial (el caso de la configuración
/// inicial, cuando todavía no hay nada que explicar). "Adjustment" -- el de §25 --
/// deja el historial intacto y registra un movimiento de ajuste por la
/// diferencia, para que se pueda ver por qué el saldo cambió.
/// </param>
public sealed record SetCashBalanceRequest(decimal Balance, string? Mode = null);

/// <summary>
/// §16-19: un gasto que la persona repite. Se DERIVA del historial en cada
/// consulta; no existe tabla ni entidad detrás, así que nunca puede quedar
/// desincronizado de los movimientos reales.
/// </summary>
/// <param name="TypicalAmount">
/// La mediana de los montos registrados con esta etiqueta, no el promedio: un
/// almuerzo de 5, 5, 5 y uno de 60 tiene mediana 5 y promedio 18,75. La mediana
/// es lo que la persona reconoce como "lo de siempre".
/// </param>
/// <param name="AmountIsReliable">
/// False cuando los montos varían tanto que proponer uno sería adivinar (§17: usa
/// el monto típico solo si es confiable). El cliente entonces abre el sheet con la
/// etiqueta puesta pero el monto en blanco, en vez de guardar de un toque.
/// </param>
public sealed record QuickEntrySuggestionDto(
    string Label,
    string Direction,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    string? CategoryColor,
    Guid FinancialAccountId,
    decimal TypicalAmount,
    bool AmountIsReliable,
    int Frequency,
    DateTimeOffset LastUsedAt);

/// <summary>
/// §19: cuando no hay suficiente historial para hablar de "frecuentes", el sheet
/// muestra "Recientes". Se devuelven ambas listas en una sola llamada para que
/// abrir el sheet no cueste dos peticiones (§40, "debe abrir prácticamente
/// instantáneo").
/// </summary>
public sealed record QuickEntryBootstrapDto(
    Guid CashAccountId,
    decimal CashBalance,
    bool CashBalanceEverSet,
    string Currency,
    IReadOnlyList<QuickEntrySuggestionDto> Frequent,
    IReadOnlyList<QuickEntrySuggestionDto> Recent);
