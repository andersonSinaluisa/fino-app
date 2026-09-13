namespace Nexo.Domain.Pulses;

/// <summary>
/// PULSO: the closed taxonomy of proactive observations FinancialPulse can
/// represent. Closed on purpose (unlike <see cref="Insights.InsightCodes"/>'s
/// open string codes) because every consumer that matters here -- severity,
/// cooldown length, relevance weighting, and later NotificationDecisionService
/// -- switches over it exhaustively; a missing case must fail to compile, the
/// same reasoning <c>NotificationDispatcher.ShouldNotify</c> already applies to
/// <see cref="Notifications.NotificationType"/>. Ten values are named because
/// that is PULSO's full product taxonomy; nine have a detector in
/// <c>PulseEngine</c> today (see its class remarks) -- only
/// <see cref="UpcomingCommitment"/> is still undetected, for lack of a real
/// "compromiso" domain model to read from. Naming every value up front, before
/// every rule has a detector, means the severity/cooldown for a future one
/// already has a documented home, instead of every addition also touching
/// every switch that reasons about "every pulse type".
/// </summary>
public enum PulseType
{
    /// <summary>Estás gastando más rápido de lo habitual este mes (sin proyectar un total).</summary>
    SpendingPace = 0,

    /// <summary>Un movimiento puntual muy por encima de lo típico del usuario.</summary>
    UnusualExpense = 1,

    /// <summary>El saldo estimado de una cuenta bajó de forma notable en pocos días.</summary>
    BalanceChange = 2,

    /// <summary>Un pago recurrente conocido está por vencer. Sin detector aún (no existe un modelo de compromisos futuros en el dominio real).</summary>
    UpcomingCommitment = 3,

    /// <summary>Estimación de cierre de mes, con cambio significativo respecto a la última proyección.</summary>
    MonthEndProjection = 4,

    /// <summary>Gastaste notablemente menos que de costumbre -- refuerzo positivo, nunca goteo diario.</summary>
    SpendingImprovement = 5,

    /// <summary>Una categoría se disparó frente a su propio promedio reciente.</summary>
    CategorySpike = 6,

    /// <summary>Llegó un ingreso nuevo o inusual.</summary>
    NewIncome = 7,

    /// <summary>Cierre del día -- solo en días con actividad real que valga la pena resumir.</summary>
    DailyClose = 8,

    /// <summary>Una cuenta de importación manual lleva demasiado tiempo sin actualizarse.</summary>
    AccountOutdated = 9,
}

/// <summary>
/// PULSO's own severity scale. Deliberately not a reuse of
/// <see cref="Insights.InsightSeverity"/>: insights are calm dashboard facts
/// that only ever read as neutral/positive/attention, but a pulse can describe
/// something that genuinely warrants the app's reserved soft-red ("riesgo real",
/// per FINO's visual language -- orange is for ordinary attention, red is not
/// spent on anything less than that). Adding the fourth case here rather than
/// widening InsightSeverity keeps that reservation meaningful for both call sites.
/// </summary>
public enum PulseSeverity
{
    Neutral = 0,
    Positive = 1,
    Attention = 2,
    Risk = 3,
}
