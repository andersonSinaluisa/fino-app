import { AnalyticsEvent, type AnalyticsEventName } from './events';

/**
 * §20: ALLOWLIST, no blacklist.
 *
 * Para cada evento se declara exactamente qué propiedades pueden salir. Todo
 * lo demás se descarta antes de llegar al proveedor. Es una allowlist porque
 * una blacklist solo bloquea lo que alguien se acordó de prohibir: el día que
 * alguien añada `{ merchantLabel }` la blacklist no lo ve venir y la
 * allowlist sí.
 *
 * El tipo es `Record<AnalyticsEventName, ...>`, así que TypeScript obliga a
 * declarar el esquema de cada evento nuevo del catálogo. Un evento sin
 * esquema no compila; no puede existir un evento que mande cualquier cosa.
 */
export const EVENT_SCHEMA: Record<AnalyticsEventName, readonly string[]> = {
  // --- Ciclo de vida -------------------------------------------------------
  [AnalyticsEvent.AppOpened]: ['source'],
  [AnalyticsEvent.AppBackgrounded]: [],
  [AnalyticsEvent.SessionStarted]: ['source'],

  // --- Autenticación -------------------------------------------------------
  [AnalyticsEvent.LoginStarted]: [],
  [AnalyticsEvent.LoginSucceeded]: [],
  [AnalyticsEvent.LoginFailed]: ['reason'],
  [AnalyticsEvent.SignupStarted]: [],
  [AnalyticsEvent.SignupSucceeded]: [],
  [AnalyticsEvent.SignupFailed]: ['reason'],
  [AnalyticsEvent.Logout]: [],

  // --- Onboarding ----------------------------------------------------------
  [AnalyticsEvent.OnboardingStarted]: [],
  [AnalyticsEvent.OnboardingStepViewed]: ['step', 'stepKey'],
  [AnalyticsEvent.OnboardingSkipped]: ['step'],
  [AnalyticsEvent.OnboardingCompleted]: [],

  // --- Banco y tutorial ----------------------------------------------------
  // `bankCode` es el código del proveedor soportado (PICHINCHA, GUAYAQUIL...),
  // no el banco del usuario descubierto de sus datos: sin él no se puede
  // responder "¿qué tutorial de banco convierte peor?", que es justo la
  // pregunta que paga este evento. Es un valor de un conjunto cerrado y
  // pequeño; nunca un nombre de cuenta ni un identificador bancario.
  [AnalyticsEvent.BankSelected]: ['bankCode'],
  [AnalyticsEvent.BankTutorialStarted]: ['bankCode'],
  [AnalyticsEvent.BankTutorialStepViewed]: ['bankCode', 'step'],
  [AnalyticsEvent.BankTutorialCompleted]: ['bankCode'],

  // --- Importación ---------------------------------------------------------
  [AnalyticsEvent.FilePickerOpened]: ['source'],
  [AnalyticsEvent.ImportStarted]: ['source', 'fileFormat'],
  [AnalyticsEvent.ImportCompleted]: [
    'source',
    'sourceType',
    'fileFormat',
    'bankCode',
    'parserVersion',
    'transactionCountBucket',
    'durationBucket',
  ],
  [AnalyticsEvent.ImportFailed]: ['source', 'fileFormat', 'bankCode', 'reason', 'durationBucket'],

  // --- Navegación ----------------------------------------------------------
  [AnalyticsEvent.HomeViewed]: ['hasData'],
  [AnalyticsEvent.MovementsViewed]: [],
  [AnalyticsEvent.MovementOpened]: ['source'],

  // --- Registro rápido -----------------------------------------------------
  [AnalyticsEvent.QuickEntryOpened]: ['source'],
  [AnalyticsEvent.QuickEntrySaved]: [
    'source',
    'entryMode',
    'transactionType',
    'categorySource',
    'offline',
    'durationBucket',
  ],
  [AnalyticsEvent.QuickEntryUndo]: ['entryMode'],
  [AnalyticsEvent.QuickEntryFailed]: ['entryMode', 'reason'],
  [AnalyticsEvent.QuickEntryDetailsOpened]: ['source', 'entryMode'],
  [AnalyticsEvent.QuickEntryQueuedOffline]: ['source', 'entryMode', 'transactionType'],
  [AnalyticsEvent.QuickEntryUndoOffline]: ['entryMode'],
  [AnalyticsEvent.QuickEntrySyncedOffline]: [],
  [AnalyticsEvent.QuickEntrySyncFailed]: ['reason'],
  [AnalyticsEvent.FrequentEntryUsed]: ['kind'],

  // --- Entrada inteligente -------------------------------------------------
  // `fieldsParsed` = cuántos campos reconoció el parser (0-3), NO el texto.
  [AnalyticsEvent.SmartEntryUsed]: ['source'],
  [AnalyticsEvent.SmartEntryParsed]: ['fieldsParsed', 'confidence'],
  [AnalyticsEvent.SmartEntryFailed]: ['reason'],

  // --- Voz -----------------------------------------------------------------
  // `transcriptLengthBucket`, nunca la longitud exacta y muchísimo menos el
  // texto: la transcripción es contenido del usuario.
  [AnalyticsEvent.VoiceEntryStarted]: ['source'],
  [AnalyticsEvent.VoiceEntryParsed]: ['transcriptLengthBucket', 'fieldsParsed'],
  [AnalyticsEvent.VoiceEntryFailed]: ['reason'],
  [AnalyticsEvent.VoicePermissionDenied]: [],

  // --- Efectivo ------------------------------------------------------------
  [AnalyticsEvent.CashAccountCreated]: ['source'],
  [AnalyticsEvent.CashBalanceSet]: [],
  [AnalyticsEvent.CashBalanceAdjusted]: [],

  // --- Conciliación --------------------------------------------------------
  [AnalyticsEvent.ReconciliationViewed]: ['source', 'suggestionCountBucket'],
  [AnalyticsEvent.TransferSuggestionShown]: ['confidence'],
  [AnalyticsEvent.TransferConfirmed]: ['confidence', 'source'],
  [AnalyticsEvent.TransferRejected]: ['confidence', 'source'],
  [AnalyticsEvent.WithdrawalDetected]: ['confidence'],
  [AnalyticsEvent.WithdrawalReviewed]: ['confidence'],
  [AnalyticsEvent.WithdrawalConfirmedAsCash]: ['confidence', 'source'],
  [AnalyticsEvent.WithdrawalRejected]: ['confidence', 'source'],

  // --- Estadísticas --------------------------------------------------------
  // `period` es el tramo elegido (month/quarter/year), no el rango de fechas.
  [AnalyticsEvent.StatisticsViewed]: ['period', 'hasData'],
  [AnalyticsEvent.StatisticsPeriodChanged]: ['period'],
  [AnalyticsEvent.StatisticsAccountFilterChanged]: ['filterKind'],

  // --- Presupuestos y Comprometido ----------------------------------------
  // Solo forma y uso: período (enum), si reserva (bool), si tiene categoría
  // (bool), prioridad (enum), nivel (enum). JAMÁS el monto, el nombre del
  // presupuesto ni el de la categoría (puede ser personalizado).
  [AnalyticsEvent.BudgetCreated]: ['period', 'reserves', 'hasCategory', 'priority'],
  [AnalyticsEvent.BudgetUpdated]: ['reserves', 'paused'],
  [AnalyticsEvent.BudgetDeleted]: [],
  [AnalyticsEvent.BudgetReserveEnabled]: ['flow'],
  [AnalyticsEvent.BudgetExceeded]: ['period'],
  [AnalyticsEvent.BudgetOpened]: ['source', 'level'],
  [AnalyticsEvent.CommittedBreakdownOpened]: ['source', 'hasData'],

  // --- Movimientos divididos ----------------------------------------------
  // `parts` es un tramo ('2' | '3' | '4_plus'), nunca montos, categorías,
  // notas, descripción bancaria, destinatarios ni referencias.
  [AnalyticsEvent.TransactionSplitCreated]: ['parts', 'transactionType', 'hasUncategorized'],
  [AnalyticsEvent.TransactionSplitUpdated]: ['parts', 'transactionType', 'hasUncategorized'],
  [AnalyticsEvent.TransactionSplitRemoved]: ['transactionType'],

  // --- Tarjetas de crédito -------------------------------------------------
  // Solo forma y uso: si reserva (bool), flujo (enum), tramo de cuotas, origen
  // del pago (cuenta/externa, nunca cuál). JAMÁS montos, deuda, cupo, últimos
  // 4 dígitos, banco/emisor, comercios, descripciones ni referencias.
  [AnalyticsEvent.CreditCardCreated]: ['autoReserve', 'flow'],
  [AnalyticsEvent.CreditCardOpened]: ['source', 'needsSetup'],
  [AnalyticsEvent.CreditCardStatementImported]: ['fileFormat', 'hasSummary'],
  [AnalyticsEvent.CreditCardPaymentRegistered]: ['paymentSource', 'flow'],
  [AnalyticsEvent.CreditCardInstallmentCreated]: ['installmentsBucket'],
  [AnalyticsEvent.CreditCardAutoReserveEnabled]: ['flow'],

  // --- Pulso ---------------------------------------------------------------
  // `pulseKind` es el TIPO de pulso (un enum del backend), nunca su texto.
  [AnalyticsEvent.PulseCardViewed]: ['pulseKind', 'source'],
  [AnalyticsEvent.PulseOpened]: ['pulseKind', 'source'],
  [AnalyticsEvent.PulseActionClicked]: ['pulseKind', 'actionKind'],
  [AnalyticsEvent.PulseDismissed]: ['pulseKind', 'source'],
  [AnalyticsEvent.PulseFeedback]: ['pulseKind', 'sentiment'],

  // --- Notificaciones ------------------------------------------------------
  [AnalyticsEvent.NotificationPermissionShown]: ['source'],
  [AnalyticsEvent.NotificationPermissionAccepted]: ['source'],
  [AnalyticsEvent.NotificationPermissionRejected]: ['source'],
  [AnalyticsEvent.NotificationOpened]: ['notificationKind'],

  // --- Proyección, cuentas, perfil -----------------------------------------
  [AnalyticsEvent.ProjectionViewed]: ['source', 'hasData'],
  [AnalyticsEvent.AccountAdded]: ['accountKind', 'source'],
  [AnalyticsEvent.AccountUpdated]: ['accountKind'],
  [AnalyticsEvent.ProfileViewed]: [],

  // --- Suscripción ---------------------------------------------------------
  [AnalyticsEvent.SubscriptionScreenViewed]: ['source'],
  [AnalyticsEvent.SubscriptionStarted]: ['plan'],
  [AnalyticsEvent.SubscriptionCompleted]: ['plan'],
  [AnalyticsEvent.SubscriptionFailed]: ['plan', 'reason'],

  // --- Escaneo de facturas (§37) -------------------------------------------
  // Lo que NUNCA sale de aquí: el monto, el comercio, la fecha exacta, el
  // texto OCR, la imagen, el RUC y el número de factura. Solo SI se detectó
  // cada cosa, que es lo que mide la calidad del OCR sin leer el recibo.
  [AnalyticsEvent.ReceiptScanOpened]: ['source'],
  [AnalyticsEvent.ReceiptCameraOpened]: [],
  [AnalyticsEvent.ReceiptGalleryOpened]: [],
  [AnalyticsEvent.ReceiptCaptureCompleted]: ['source'],
  [AnalyticsEvent.ReceiptAnalysisStarted]: ['source'],
  [AnalyticsEvent.ReceiptAnalysisCompleted]: [
    'source',
    'analysisResult',
    'totalDetected',
    'dateDetected',
    'merchantDetected',
    'paymentHintDetected',
    'durationBucket',
  ],
  [AnalyticsEvent.ReceiptAnalysisFailed]: ['source', 'reason', 'durationBucket'],
  [AnalyticsEvent.ReceiptReviewOpened]: ['source', 'analysisResult'],
  [AnalyticsEvent.ReceiptSaved]: ['source', 'analysisResult', 'matchedExisting', 'durationBucket'],
  [AnalyticsEvent.ReceiptMatchedExistingTransaction]: ['confidence', 'ambiguous'],
  [AnalyticsEvent.ReceiptPossibleDuplicateShown]: [],
  [AnalyticsEvent.ReceiptRetry]: ['source', 'reason'],

  // --- Hitos únicos --------------------------------------------------------
  [AnalyticsEvent.FirstValueReached]: ['valueSource'],
  [AnalyticsEvent.FirstImportCompleted]: ['fileFormat', 'bankCode'],
  [AnalyticsEvent.FirstCashEntry]: ['entryMode'],
  [AnalyticsEvent.FirstPulseOpened]: ['pulseKind'],
  [AnalyticsEvent.FirstReceiptScanned]: ['source'],

  // --- Errores de producto -------------------------------------------------
  // `scope` = en qué flujo pasó. NUNCA el mensaje ni el stack: eso es trabajo
  // de un error monitor (§19), no de product analytics.
  [AnalyticsEvent.AppError]: ['scope', 'reason'],
};

/**
 * Las ÚNICAS claves que pueden llevar un número. Todo lo demás cuantitativo
 * viaja bucketizado como string.
 *
 * Es la defensa que de verdad importa: si cualquier clave pudiera llevar
 * números, un `amount` mal nombrado o un saldo redondeado podrían colarse
 * por una propiedad que sí está en la allowlist. Aquí un número solo puede
 * ser un índice de paso o un conteo de campos reconocidos, ambos acotados.
 */
export const NUMERIC_KEYS: ReadonlySet<string> = new Set(['step', 'fieldsParsed']);

/** Rango aceptado para esas claves numéricas. Fuera de aquí se descarta. */
export const NUMERIC_MIN = 0;
export const NUMERIC_MAX = 100;

/**
 * §21: red secundaria. Si una clave de la allowlist empieza a parecerse a
 * dato sensible, se descarta igualmente y se avisa en desarrollo — sería un
 * error de quien escribió el esquema, y es mejor perder una métrica que
 * filtrar un dato.
 */
export const SUSPICIOUS_KEY_PATTERN =
  /amount|monto|balance|saldo|email|correo|\bname\b|nombre|account|cuenta|description|descripcion|merchant|comercio|recipient|destinatario|sender|remitente|iban|card|tarjeta|document|cedula|voice|transcript|transcripcion|filename|file_?name|archivo|token|phone|telefono|address|direccion/i;

/**
 * Excepciones explícitas al patrón anterior: claves que lo disparan por su
 * forma pero son valores de conjunto cerrado y sin contenido del usuario.
 * Cada una está justificada en docs/analytics.md.
 */
export const SUSPICIOUS_PATTERN_EXCEPTIONS: ReadonlySet<string> = new Set([
  'accountKind', // 'bank' | 'cash' | 'card' -- el tipo, no la cuenta
  'transactionType', // 'expense' | 'income'
  'transcriptLengthBucket', // tramo de longitud, nunca el texto
  // Booleano: SI se detectó un comercio, jamás cuál. Es la métrica de
  // calidad del OCR (§38) y sin ella no se puede saber si el parser sirve.
  'merchantDetected',
]);
