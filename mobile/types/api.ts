/** Mirrors the DTOs in Nexo.Application. Keep both sides in sync deliberately. */

export type TransactionDirection = 'Income' | 'Expense';
export type TransactionSource = 'Import' | 'Email' | 'Api' | 'Webhook' | 'Manual';
export type TransactionStatus = 'Posted' | 'Pending' | 'NeedsReview' | 'Ignored';
export type BalanceType = 'Verified' | 'Estimated';
export type ConnectionMode = 'ManualImport' | 'Email' | 'Api' | 'Webhook';
export type AccountType = 'Checking' | 'Savings' | 'CreditCard' | 'Wallet' | 'Other' | 'Cash';

/**
 * Tarjetas de crédito: qué es un movimiento de una tarjeta. Solo lo tienen los
 * movimientos de una cuenta CreditCard (null en cualquier otra). Qué cuenta
 * como gasto o no lo decide el backend (CreditCardMovementRules); la app solo
 * lo muestra.
 */
export type CardMovementType = 'Purchase' | 'Refund' | 'Payment' | 'Interest' | 'Fee' | 'CashAdvance' | 'Adjustment';

/**
 * Onboarding funcional (rediseño post-login): the five independent milestones
 * -- never a single boolean, since "saw the tutorial" and "has an account"
 * and "has imported something" are distinct facts that can be out of sync
 * (someone can skip the tutorial and still add an account later from
 * Cuentas). `hasImportedData` mirrors the backend's own computed field so
 * Home's "show the empty state or not" decision never has to be
 * reimplemented on this side.
 */
export interface OnboardingStatus {
  startedAt: string | null;
  tutorialCompletedAt: string | null;
  skippedAt: string | null;
  firstAccountAddedAt: string | null;
  firstImportCompletedAt: string | null;
  hasImportedData: boolean;
}

export interface AuthenticatedUser {
  id: string;
  email: string;
  displayName: string;
  timeZoneId: string;
  currency: string;
  locale: string;
  onboarding: OnboardingStatus;
}

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthenticatedUser;

  /**
   * Entregable 22 ("Privacidad completa"): true when this login undid a
   * pending "eliminar mi cuenta" request that was still inside its grace
   * period. Always false otherwise, including on refresh/register.
   */
  accountDeletionCancelled: boolean;
}

export interface ProviderCapability {
  mode: ConnectionMode;
  label: string;
  isAutomatic: boolean;
}

export interface Provider {
  code: string;
  name: string;
  shortName: string;
  kind: 'Bank' | 'Wallet';
  brandColor: string;
  logoKey: string | null;
  capabilities: ProviderCapability[];
  defaultMode: ConnectionMode;
  supportsStatementImport: boolean;
}

export interface Account {
  id: string;
  providerCode: string;
  providerName: string;
  brandColor: string;
  logoKey: string | null;
  alias: string;
  accountType: AccountType;
  mask: string | null;
  currency: string;
  connectionMode: ConnectionMode;
  balance: number;
  balanceType: BalanceType;
  lastVerifiedBalance: number | null;
  lastVerifiedAt: string | null;
  lastTransactionAt: string | null;
  lastSyncedAt: string | null;
  isArchived: boolean;
  /**
   * Tarjetas de crédito: true para una tarjeta. Su `balance` es deuda (negativo
   * cuando se debe) y nunca forma parte de "Tu dinero".
   */
  isLiability: boolean;
}

export interface TransactionListItem {
  id: string;
  financialAccountId: string;
  accountAlias: string;
  providerCode: string;
  brandColor: string;
  transactionDate: string;
  amount: number;
  signedAmount: number;
  currency: string;
  direction: TransactionDirection;
  description: string;
  merchant: string | null;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColor: string | null;
  status: TransactionStatus;
  source: TransactionSource;
  /**
   * Entregable 13: true once the person confirmed this is one leg of a
   * transfer between their own accounts. It still moved the account's
   * balance, but the backend excludes it from net income/expense totals and
   * insights -- moving your own money is neither spending nor earning.
   */
  isInternalTransfer: boolean;
  /**
   * Movimientos divididos: cuando es true, `categoryId` es null y `splits`
   * trae las partes. Sigue siendo UN movimiento en cualquier lista.
   */
  isSplit: boolean;
  splits: TransactionSplit[];
  /** Tarjetas de crédito: compra, pago, devolución... null fuera de una tarjeta. */
  cardMovementType: CardMovementType | null;
}

/**
 * Una parte de un movimiento dividido. `amount` es magnitud positiva, igual
 * que el `amount` del movimiento (el signo vive en `direction`).
 * `categoryId` null = "Sin categoría".
 */
export interface TransactionSplit {
  id: string;
  categoryId: string | null;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  amount: number;
  note: string | null;
}

/** PUT /transactions/{id}/splits: la división COMPLETA, reemplazada de forma atómica. */
export interface ReplaceSplitsRequest {
  splits: { categoryId: string | null; amount: number; note?: string | null }[];
  /** La versión que se editó; si otra edición llegó antes, el backend responde 409. */
  expectedVersion?: number;
}

/**
 * "Categorización personal": por qué una transacción tiene la categoría que
 * tiene, para que la UI lo pueda explicar (punto 11) en vez de dejar que la
 * persona adivine.
 */
export type CategorySource = 'Uncategorized' | 'SystemRule' | 'UserRule' | 'Imported' | 'Manual' | 'ManualSplit';

export interface TransactionDetail extends Omit<TransactionListItem, 'brandColor' | 'categoryIcon' | 'categoryColor'> {
  providerName: string;
  accountMask: string | null;
  /**
   * Entregable 12: presente solo cuando la persona corrigió el comercio a
   * mano. `merchant` ya refleja la corrección cuando existe -- este campo es
   * metadata para la UI (mostrar el aviso "corregido", saber si aplica Borrar).
   */
  merchantCorrected: string | null;
  categoryManuallySet: boolean;
  /** "Categorización personal" (punto 11): de dónde salió la categoría actual. */
  categorySource: CategorySource;
  /** Presente solo cuando `categorySource` es `UserRule` o `SystemRule`. */
  categorizationRuleId: string | null;
  externalReference: string | null;
  sourceConfidence: 'Low' | 'Medium' | 'High';
  note: string | null;
  possibleDuplicateOfId: string | null;
  importId: string | null;
  createdAt: string;
  /** Entregable 13: the matching movement on the other account, once confirmed. */
  internalTransferLinkId: string | null;
  /**
   * "Categorización personal" (punto 7): presente solo en la respuesta de
   * PUT .../category cuando se pidió aplicar también a movimientos
   * anteriores -- cuántos OTROS movimientos se recategorizaron. `null` en
   * cualquier otra lectura de la transacción.
   */
  recategorizedCount: number | null;
  /** Movimientos divididos: versión de la división, para detectar ediciones simultáneas. */
  splitVersion: number;
  /** Tarjetas de crédito: presente cuando esta compra está diferida en cuotas. */
  installmentPlanId: string | null;
  /** Tarjetas de crédito: el movimiento es de una tarjeta (ofrece tipo, cuotas...). */
  accountIsCreditCard: boolean;
}

/**
 * "Categorización personal": una regla propia del usuario (punto 3/15),
 * "Reglas de categorización" en Ajustes. Nunca incluye reglas de otro
 * usuario ni reglas del sistema (esas no tienen pantalla propia todavía).
 */
export interface CategorizationRule {
  id: string;
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  categoryId: string;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  isActive: boolean;
  priority: number;
  matchCount: number;
  createdAt: string;
  updatedAt: string;
  lastMatchedAt: string | null;
}

export interface CreateCategorizationRuleRequest {
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  categoryId: string;
}

export interface UpdateCategorizationRuleRequest {
  categoryId: string;
  isActive?: boolean;
  /** Punto 16: nunca se recategoriza el historial salvo que se pida explícitamente. */
  applyToExistingMatches?: boolean;
}

/** Punto 19: "si creo esta regla, ¿qué movimientos coincidirían?", antes de guardar nada. */
export interface RulePreviewRequest {
  transactionId: string;
  categoryId: string;
  /** La parte de la descripción que eligió la persona; sin ella, el backend sugiere. */
  pattern?: string | null;
}

export interface RulePreviewTransaction {
  id: string;
  description: string;
  signedAmount: number;
  transactionDate: string;
}

export interface RulePreview {
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  /** Punto 18: el patrón sugerido es demasiado genérico -- Nexo no ofrecerá crear la regla. */
  isTooGeneric: boolean;
  matchedCount: number;
  sample: RulePreviewTransaction[];
  /** Punto 13: ya existe una regla propia con este mismo patrón, para otra categoría. */
  conflictingRuleId: string | null;
  conflictingCategoryId: string | null;
  conflictingCategoryName: string | null;
  /**
   * El texto exacto con el que se compara cada regla: la descripción en
   * mayúsculas, sin tildes ni números de referencia. `pattern` siempre es una
   * parte de este texto. Vacío en un backend anterior a este campo.
   */
  normalizedDescription?: string;
  /** Lo que Fino elegiría solo, para "volver a la sugerencia". */
  suggestedPattern?: string;
}

/**
 * Entregable 13: one suggested pair -- an outflow from one account and an
 * inflow into another, same amount, close in time, probably the same money
 * moving between the person's own accounts.
 */
export interface InternalTransferCandidate {
  outgoingTransactionId: string;
  outgoingAccountId: string;
  outgoingAccountAlias: string;
  outgoingDate: string;
  outgoingDescription: string;
  incomingTransactionId: string;
  incomingAccountId: string;
  incomingAccountAlias: string;
  incomingDate: string;
  incomingDescription: string;
  amount: number;
  currency: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
  totalPages: number;
}

export interface Category {
  id: string;
  code: string;
  name: string;
  icon: string;
  color: string;
  isSystem: boolean;
  isIncome: boolean;
}

/** Categorías personalizadas: icon/color deben ser uno de los valores de CATEGORY_ICON_KEYS/CATEGORY_COLORS -- el backend los valida igual, nunca confíes solo en el picker. */
export interface CreateCategoryRequest {
  name: string;
  icon: string;
  color: string;
  isIncome?: boolean;
}

export interface UpdateCategoryRequest {
  name: string;
  icon: string;
  color: string;
}

export interface CategoryBreakdownItem {
  categoryId: string;
  name: string;
  icon: string;
  color: string;
  total: number;
  percentage: number;
  count: number;
}

export interface Insight {
  code: string;
  title: string;
  body: string;
  value: number | null;
  comparisonValue: number | null;
  percentChange: number | null;
  severity: 'Neutral' | 'Positive' | 'Attention';
  referenceId: string | null;
  periodStart: string;
  periodEnd: string;
  /**
   * Entregable 16 ("Insights v1"): the backend already never returns an insight
   * past this instant, so the app has no need to check it itself -- it rides
   * along for API-shape completeness (e.g. a client that caches this response).
   */
  validUntil: string;
}

/**
 * PULSO: a proactive, explainable observation from PulseEngine. Unlike
 * Insight (a replaceable dashboard snapshot), a pulse is a permanent event --
 * it has an id and stays in history once created.
 */
export interface Pulse {
  id: string;
  type:
    | 'SpendingPace'
    | 'UnusualExpense'
    | 'BalanceChange'
    | 'UpcomingCommitment'
    | 'MonthEndProjection'
    | 'SpendingImprovement'
    | 'CategorySpike'
    | 'NewIncome'
    | 'DailyClose'
    | 'AccountOutdated';
  severity: 'Neutral' | 'Positive' | 'Attention' | 'Risk';
  title: string;
  body: string;
  /** "¿Por qué veo esto?" -- the detail screen's reasoning section. */
  explanation: string;
  value: number | null;
  comparisonValue: number | null;
  percentChange: number | null;
  referenceId: string | null;
  relevanceScore: number;
  periodStart: string;
  periodEnd: string;
  occurredAt: string;
  createdAt: string;
  /** PULSO FASE 4: 👍/👎 left on the detail screen, or null if nobody has yet. */
  feedbackHelpful: boolean | null;
}

/**
 * Entregable 15 ("Dashboard final MVP"): "comparación mensual" as a guaranteed
 * dashboard figure. The percent fields are null when there is nothing from the
 * previous month to compare against.
 */
export interface MonthComparison {
  previousIncome: number;
  previousExpense: number;
  incomeChangePercent: number | null;
  expenseChangePercent: number | null;
}

/** Entregable 15: one entry per manually-imported account overdue for a sync. */
export interface StaleAccount {
  accountId: string;
  alias: string;
  lastSyncedAt: string | null;
}

export interface HomeSummary {
  greeting: string;
  displayName: string;
  totalBalance: number;
  currency: string;
  accountCount: number;
  anyEstimatedBalance: boolean;
  month: { income: number; expense: number; net: number };
  monthComparison: MonthComparison;
  accounts: Account[];
  recentTransactions: TransactionListItem[];
  categoryBreakdown: CategoryBreakdownItem[];
  staleAccounts: StaleAccount[];
  insights: Insight[];
}

export interface ImportPreviewRow {
  id: string;
  rowNumber: number;
  transactionDate: string | null;
  amount: number | null;
  direction: TransactionDirection | null;
  description: string | null;
  externalReference: string | null;
  status: 'Ready' | 'ExactDuplicate' | 'ProbableDuplicate' | 'Invalid' | 'Imported' | 'Skipped';
  matchType: 'NoMatch' | 'ProbableMatch' | 'ExactMatch';
  matchedTransactionId: string | null;
  suggestedCategoryId: string | null;
  suggestedCategoryName: string | null;
  error: string | null;
  /** Tarjetas de crédito: por qué la fila se deja fuera a propósito (cuota de un diferido ya registrado). */
  skipReason: string | null;
}

/** Tarjetas de crédito: las cifras del encabezado de un estado de tarjeta. */
export interface CardStatementSummary {
  closingDate: string | null;
  dueDate: string | null;
  statementBalance: number | null;
  minimumPayment: number | null;
  creditLimit: number | null;
}

export interface ImportPreview {
  importId: string;
  financialAccountId: string;
  fileName: string;
  parserCode: string | null;
  /**
   * El código de banco (Provider.code, sin sufijo de versión) que el parser
   * de verdad reconoció -- null para el parser genérico/mapeo manual, que no
   * afirman reconocer ningún banco. Se usa para avisar "este archivo parece
   * ser de otro banco" comparando contra la cuenta seleccionada, sin adivinar
   * el mapeo `parserCode` (p. ej. "GUAYAQUIL_V1") -> código de proveedor.
   */
  detectedProviderCode: string | null;
  status: 'Received' | 'PreviewReady' | 'Completed' | 'Failed' | 'Cancelled';
  totalRows: number;
  newRows: number;
  duplicateRows: number;
  probableDuplicateRows: number;
  invalidRows: number;
  incomeTotal: number;
  expenseTotal: number;
  periodStart: string | null;
  periodEnd: string | null;
  declaredClosingBalance: number | null;
  failureReason: string | null;
  /** True when an identical file was already imported into this account. */
  previouslyImportedFile: boolean;
  rows: ImportPreviewRow[];
  /**
   * Entregable 10 (mapeo manual de columnas): presentes solo cuando
   * status === 'Failed' porque ningún parser reconoció el archivo.
   * unmappedColumns es la primera fila muestreada del archivo (a menudo el
   * encabezado real, pero no se asume que lo sea); unmappedSampleRows son las
   * filas siguientes, para mostrar una grilla cruda y dejar que la persona
   * asigne cada columna a fecha/descripción/monto/etc.
   */
  unmappedColumns?: string[] | null;
  unmappedSampleRows?: string[][] | null;
  /** Tarjetas de crédito: corte, fecha máxima, total, mínimo y cupo leídos del estado. */
  cardStatement?: CardStatementSummary | null;
}

/** Elección de columnas que la persona hace a mano para un archivo no reconocido. */
export interface ManualColumnMapping {
  firstRowIsHeader: boolean;
  dateColumn: number;
  descriptionColumn: number;
  amountColumn?: number | null;
  debitColumn?: number | null;
  creditColumn?: number | null;
  referenceColumn?: number | null;
  saveMapping: boolean;
}

export interface ImportResult {
  importId: string;
  importedCount: number;
  skippedDuplicates: number;
  flaggedForReview: number;
  newEstimatedBalance: number;
  balanceType: BalanceType;
  /** Entregable 27: of skippedDuplicates, how many folded their exact-match
   * statement data into an existing movement (typically one email created
   * earlier) instead of being a plain no-op skip. */
  upgradedCount: number;
}

/** One row of the import history screen (Entregable 8). */
export interface ImportSummary {
  importId: string;
  financialAccountId: string;
  accountAlias: string;
  fileName: string;
  status: 'Received' | 'PreviewReady' | 'Completed' | 'Failed' | 'Cancelled';
  createdAt: string;
  newRows: number;
  duplicateRows: number;
  probableDuplicateRows: number;
  invalidRows: number;
  importedCount: number;
}

export interface EmailConnection {
  id: string;
  providerKind: 'Gmail' | 'Outlook' | 'Forwarding';
  emailAddress: string;
  status: string;
  inboundAddress: string | null;
  connectedAt: string | null;
  lastSyncedAt: string | null;
  transactionsDetected: number;
  isAvailable: boolean;
  unavailableReason: string | null;
}

/**
 * Entregable 17 ("Notificaciones"): una categoría por cada tipo de aviso que la
 * persona puede prender o apagar de forma independiente -- Movimientos,
 * Ingresos, Insights, Seguridad, Recordatorios. `pushEnabled` y
 * `showAmountsInPreview` no son categorías: el primero apaga el push del todo
 * en este dispositivo, el segundo es la privacidad de la vista previa
 * (Entregable 18). PULSO FASE 3 agrega la sexta categoría (`notifyOnPulses`)
 * y el horario "no molestar" (`quietHoursStartHour`/`quietHoursEndHour`,
 * ambos null cuando está apagado, o una hora 0-23 en hora de Ecuador).
 */
export interface NotificationPreferences {
  pushEnabled: boolean;
  showAmountsInPreview: boolean;
  notifyOnMovements: boolean;
  notifyOnIncome: boolean;
  notifyOnInsights: boolean;
  notifyOnSecurity: boolean;
  notifyOnReminders: boolean;
  notifyOnPulses: boolean;
  quietHoursStartHour: number | null;
  quietHoursEndHour: number | null;
}

/** Entregable 17: one row of the in-app notification list. */
export interface AppNotification {
  id: string;
  type: string;
  title: string;
  body: string;
  isRead: boolean;
  createdAt: string;
  /** Deep-link keys (cardId, budgetId, accountId, screen, transactionId, pulseId). */
  data?: Record<string, string> | null;
}

/**
 * Entregable 19 ("Sesiones y dispositivos"): one active refresh token, which --
 * since it is single-use and rotates on every use -- is exactly one device's
 * currently open session. `createdAt` is that row's own issuance time, so for
 * a session that has been quietly rotating in the background it reads as
 * "last active" rather than "logged in since".
 */
/**
 * Entregable 21 ("Auditoría"): one row of the user's own account activity.
 * `label` is the sentence to show -- already in Spanish, built server-side so
 * this type never has to duplicate the action-to-sentence mapping. `action` is
 * only the raw code (e.g. "user.logged_in"), kept around for picking an icon.
 */
export interface SecurityEvent {
  id: string;
  action: string;
  label: string;
  createdAt: string;
  succeeded: boolean;
}

export interface Session {
  id: string;
  deviceLabel: string | null;
  userAgent: string | null;
  createdAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

/** Dashboard de estadísticas -- ventanas de tiempo que el backend entiende. */
export type AnalyticsPeriodCode = 'month' | 'last_month' | 'last_3_months' | 'last_6_months' | 'year' | 'custom';

export interface AnalyticsPeriodInfo {
  period: AnalyticsPeriodCode;
  label: string;
  from: string;
  to: string;
}

/**
 * Los 4 KPIs del dashboard. Los `*ChangePercent` son null cuando no hay nada
 * en el periodo anterior con qué comparar -- nunca un 0% inventado.
 */
export interface AnalyticsKpis {
  income: number;
  expense: number;
  net: number;
  savingsRatePercent: number | null;
  incomeChangePercent: number | null;
  expenseChangePercent: number | null;
  netChangePercent: number | null;
}

/**
 * Fijo/variable no es una suposición por categoría: un comercio solo cuenta
 * como fijo si de verdad se repitió en al menos 2 de los últimos 6 meses con
 * un monto parecido (ver `RecurringPayment`).
 */
export interface MoneyFlow {
  income: number;
  fixedExpense: number;
  variableExpense: number;
  netSavings: number;
  savingsRatePercent: number | null;
}

export interface AnalyticsSeriesPoint {
  from: string;
  to: string;
  label: string;
  income: number;
  expense: number;
}

/**
 * Un saldo real reconstruido a partir del saldo actual menos los movimientos
 * que pasaron después de ese momento -- nunca una proyección hacia adelante.
 */
export interface BalancePoint {
  asOf: string;
  label: string;
  balance: number;
}

export interface CategoryTrend {
  categoryId: string;
  name: string;
  icon: string;
  color: string;
  total: number;
  percentage: number;
  count: number;
  previousTotal: number | null;
  changePercent: number | null;
}

/** Un comercio que apareció en al menos 2 de los últimos 6 meses con un monto estable. */
export interface RecurringPayment {
  merchant: string;
  categoryName: string | null;
  averageAmount: number;
  occurrencesLast6Months: number;
  lastSeenAt: string;
}

export interface MerchantRanking {
  merchant: string;
  total: number;
  count: number;
}

export interface DailySpend {
  date: string;
  total: number;
}

export interface WeekdayWeekend {
  weekdayTotal: number;
  weekdayAveragePerDay: number;
  weekendTotal: number;
  weekendAveragePerDay: number;
}

export interface MonthlyHistoryItem {
  monthLabel: string;
  income: number;
  expense: number;
  net: number;
}

export interface AnalyticsDashboard {
  period: AnalyticsPeriodInfo;
  kpis: AnalyticsKpis;
  moneyFlow: MoneyFlow;
  series: AnalyticsSeriesPoint[];
  balanceEvolution: BalancePoint[];
  categoryBreakdown: CategoryTrend[];
  spotlightCategoryId: string | null;
  recurringPayments: RecurringPayment[];
  topMerchants: MerchantRanking[];
  peakSpendingDays: DailySpend[];
  weekdayWeekend: WeekdayWeekend;
  monthlyHistory: MonthlyHistoryItem[];
  insights: Insight[];
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
  /**
   * "Categorización personal" (punto 13): presentes solo cuando `code` es
   * `rule_conflict` -- la regla existente con la que choca el patrón nuevo,
   * para ofrecer "Actualizar regla existente" en vez de crear un duplicado.
   */
  existingRuleId?: string;
  existingCategoryId?: string;
  existingCategoryName?: string;
}

/* -------------------------------------------------------------------------
   Registro rápido de efectivo
   ------------------------------------------------------------------------- */

/**
 * §36: el identificador lo genera el CLIENTE antes de mandar la petición. Si el
 * usuario toca Guardar dos veces, las dos peticiones llevan el mismo valor y el
 * servidor devuelve el movimiento ya creado en lugar de crear otro.
 */
export interface CreateQuickTransactionRequest {
  amount: number;
  direction?: TransactionDirection;
  description?: string | null;
  financialAccountId?: string | null;
  /**
   * Ojo con la diferencia: `undefined` significa "decide tú" y deja que el motor
   * de reglas del servidor categorice; `leaveUncategorized: true` significa "la
   * persona quitó la categoría a propósito", y entonces ninguna regla se la
   * vuelve a poner.
   */
  categoryId?: string | null;
  leaveUncategorized?: boolean;
  occurredAt?: string | null;
  clientRequestId?: string;
  note?: string | null;
  /** Tarjetas de crédito: solo en una tarjeta. Sin valor, el backend lo decide. Los pagos van por "Pagar tarjeta". */
  cardMovementType?: Exclude<CardMovementType, 'Payment'> | null;
}

export interface UpdateQuickTransactionRequest {
  amount: number;
  direction?: TransactionDirection;
  description?: string | null;
  categoryId?: string | null;
  leaveUncategorized?: boolean;
  occurredAt?: string | null;
  cardMovementType?: Exclude<CardMovementType, 'Payment'> | null;
}

/** §24-25: "Anchor" ancla el saldo; "Adjustment" registra la diferencia como movimiento. */
export interface SetCashBalanceRequest {
  balance: number;
  mode?: 'Anchor' | 'Adjustment';
}

/**
 * §16-19: un gasto que la persona repite. Se deriva del historial en el servidor;
 * no hay tabla detrás, así que nunca queda desincronizado de los movimientos.
 */
export interface QuickEntrySuggestion {
  label: string;
  direction: TransactionDirection;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColor: string | null;
  financialAccountId: string;
  typicalAmount: number;
  /** §17: false cuando los montos varían tanto que proponer uno sería adivinar. */
  amountIsReliable: boolean;
  frequency: number;
  lastUsedAt: string;
}

export interface QuickEntryBootstrap {
  /** Guid vacío mientras la cuenta de efectivo todavía no existe: se crea al guardar. */
  cashAccountId: string;
  cashBalance: number;
  cashBalanceEverSet: boolean;
  currency: string;
  frequent: QuickEntrySuggestion[];
  recent: QuickEntrySuggestion[];
}

/* -------------------------------------------------------------------------
   Retiros de efectivo
   ------------------------------------------------------------------------- */

/** "High" se sugiere con fuerza; "Medium" se pregunta de forma neutra. */
export type WithdrawalConfidence = 'High' | 'Medium';

/**
 * Un movimiento bancario que parece un retiro. Un retiro conciliado NO es un tipo
 * nuevo de movimiento: se convierte en una transferencia interna normal, la misma
 * que ya excluye Fino de todas sus métricas de gasto.
 */
export interface WithdrawalCandidate {
  transactionId: string;
  financialAccountId: string;
  accountAlias: string;
  providerCode: string;
  transactionDate: string;
  description: string;
  amount: number;
  currency: string;
  confidence: WithdrawalConfidence;
  matchedTerm: string | null;
  /** El ingreso de efectivo ya registrado que probablemente ES este retiro. */
  suggestedCashTransactionId: string | null;
  suggestedCashDate: string | null;
  /** 2 o más significa que hay coincidencias pero Fino no puede elegir por su cuenta. */
  ambiguousMatchCount: number;
  /** False cuando el texto del banco es demasiado genérico para anclar una regla. */
  canLearnPattern: boolean;
  timesConfirmedBefore: number;
}

export interface ConfirmWithdrawalRequest {
  /** Sin esto, Fino CREA la entrada de efectivo que falta. */
  cashTransactionId?: string | null;
  rememberPattern?: boolean;
}

export interface WithdrawalConfirmation {
  bankTransactionId: string;
  cashTransactionId: string;
  cashAccountId: string;
  cashAccountAlias: string;
  amount: number;
  currency: string;
  cashAccountCreated: boolean;
  matchedExistingCashMovement: boolean;
  patternRemembered: boolean;
}

export interface WithdrawalScan {
  candidateCount: number;
  highConfidenceCount: number;
}

// ---------------------------------------------------------------------------
// Presupuestos y Comprometido. Mirrors Nexo.Application.Budgets.BudgetContracts.
// Every figure here is computed by the backend (BudgetCalculator /
// CommittedMoneyCalculator); the app only formats and displays them.
// ---------------------------------------------------------------------------

export type BudgetPeriod = 'Weekly' | 'Biweekly' | 'Monthly' | 'Custom';
export type BudgetPriority = 'Essential' | 'Important' | 'Flexible';
/** Normal < 70% · Attention 70–89% · NearLimit 90–99% · Exceeded ≥ 100%. Decided by the backend. */
export type BudgetUsageLevel = 'Normal' | 'Attention' | 'NearLimit' | 'Exceeded';

export interface BudgetWindow {
  /** Local calendar date, yyyy-MM-dd. */
  start: string;
  end: string;
  /** "Septiembre 2026" or "7 – 13 sept". */
  label: string;
}

export interface BudgetProgress {
  amount: number;
  spent: number;
  remaining: number;
  overspent: number;
  percentUsed: number;
  level: BudgetUsageLevel;
  /** What this budget currently adds to Comprometido. */
  reserved: number;
  daysInWindow: number;
  daysElapsed: number;
  daysRemaining: number;
  dailyAllowance: number | null;
  projectedSpend: number | null;
  isCurrentWindow: boolean;
}

export interface Budget {
  id: string;
  name: string;
  /** Current amount of the definition; `progress.amount` is the one that applied to the shown window. */
  amount: number;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColor: string | null;
  period: BudgetPeriod;
  startDate: string;
  endDate: string | null;
  isRecurring: boolean;
  reserveFunds: boolean;
  priority: BudgetPriority;
  isActive: boolean;
  currency: string;
  window: BudgetWindow | null;
  progress: BudgetProgress | null;
  createdAt: string;
  updatedAt: string;
}

export interface BudgetInsight {
  kind: 'Exceeded' | 'PaceWillExceed' | 'HighUsage' | 'DailyAllowance' | 'ComparedToPrevious';
  budgetId: string;
  message: string;
  /** Same insight without money figures, for "ocultar montos". */
  messageWithoutAmounts: string;
}

export interface BudgetOverview {
  date: string;
  label: string;
  isCurrent: boolean;
  totals: { budgeted: number; spent: number; remaining: number; reserved: number; exceededCount: number };
  budgets: Budget[];
  topInsight: BudgetInsight | null;
}

export interface BudgetHistoryItem {
  window: BudgetWindow;
  amount: number;
  spent: number;
  percentUsed: number;
  level: BudgetUsageLevel;
}

export interface BudgetDetail {
  budget: Budget;
  insights: BudgetInsight[];
  history: BudgetHistoryItem[];
}

export interface BudgetMovements {
  window: BudgetWindow | null;
  items: TransactionListItem[];
}

export interface CreateBudgetRequest {
  amount: number;
  categoryId?: string | null;
  name?: string | null;
  period?: BudgetPeriod;
  startDate?: string | null;
  endDate?: string | null;
  isRecurring?: boolean;
  reserveFunds: boolean;
  priority?: BudgetPriority;
}

export interface UpdateBudgetRequest {
  amount: number;
  categoryId: string | null;
  name?: string | null;
  endDate?: string | null;
  reserveFunds: boolean;
  priority?: BudgetPriority;
  isActive: boolean;
}

export interface BudgetPreviewRequest {
  amount: number;
  categoryId?: string | null;
  period?: BudgetPeriod;
  startDate?: string | null;
  endDate?: string | null;
  isRecurring?: boolean;
  reserveFunds: boolean;
  /** Present when previewing an edit, so the budget's current reserve is replaced, not added twice. */
  budgetId?: string | null;
}

export interface BudgetPreview {
  currentMoney: number;
  committedNow: number;
  availableNow: number;
  budgetContribution: number;
  committedAfter: number;
  availableAfter: number;
  overcommittedAfter: number;
  alreadySpent: number;
}

export type CommittedSourceType = 'reserved_budget' | 'upcoming_payment' | 'credit_card';

export interface CommittedItem {
  label: string;
  amount: number;
  grossAmount: number;
  budgetId: string | null;
  categoryId: string | null;
  coveredByBudgetName: string | null;
  /** Próximos pagos: fecha estimada. Tarjetas: fecha máxima de pago. */
  expectedDate: string | null;
  /** Tarjetas: la tarjeta (id de su cuenta). */
  creditCardId: string | null;
}

export interface CommittedSource {
  type: CommittedSourceType;
  label: string;
  description: string;
  amount: number;
  items: CommittedItem[];
}

/** GET /api/v1/finance/committed -- the ONLY source of Tu dinero / Comprometido / Disponible. */
export interface CommittedMoney {
  currentMoney: number;
  committed: number;
  available: number;
  overcommitted: number;
  isOvercommitted: boolean;
  currency: string;
  sources: CommittedSource[];
  daysRemainingInMonth: number;
  dailyAvailable: number | null;
}

// ---------------------------------------------------------------- Tarjetas de crédito
// Todas las cifras salen de CreditCardCalculator en el backend; la app no
// calcula deuda, cupo, pagado, pendiente ni lo que va a Comprometido.

export type CardNetwork = 'Visa' | 'Mastercard' | 'AmericanExpress' | 'Diners' | 'Discover' | 'Other';
export type StatementStatus = 'Open' | 'Closed' | 'PartiallyPaid' | 'Paid' | 'Overdue';

export interface CreditCardNextPayment {
  amount: number;
  dueDate: string;
  minimumPayment: number | null;
  /** "statement": lo pendiente de un estado cerrado. "current_cycle": proyección del ciclo abierto. */
  source: 'statement' | 'current_cycle';
  isOverdue: boolean;
  daysUntilDue: number;
}

export interface CreditCardSummary {
  /** El id de la CUENTA de la tarjeta (el mismo que usan sus movimientos). */
  id: string;
  name: string;
  providerCode: string;
  providerName: string;
  brandColor: string;
  lastFour: string | null;
  currency: string;
  network: CardNetwork | null;
  isArchived: boolean;
  /** Tarjeta creada antes del módulo: falta cupo/corte/pago. */
  needsSetup: boolean;
  creditLimit: number | null;
  closingDay: number | null;
  paymentDueDay: number | null;
  autoReserve: boolean;
  currentDebt: number;
  creditBalance: number;
  /** Cupo disponible: crédito, NUNCA dinero. */
  availableCredit: number | null;
  utilizationPercent: number | null;
  isOverLimit: boolean;
  /** Deuda en cuotas de estados futuros. */
  deferredDebt: number;
  nextPayment: CreditCardNextPayment | null;
  committedContribution: number;
  balanceType: BalanceType;
  lastSyncedAt: string | null;
}

export interface CreditCardList {
  cards: CreditCardSummary[];
  totalDebt: number;
  totalNextPayments: number;
  totalCommitted: number;
  currency: string;
}

export interface CreditCardCycle {
  start: string;
  closing: string;
  due: string;
  projectedBalance: number;
  charges: number;
  credits: number;
}

export interface CreditCardStatement {
  periodStart: string;
  closingDate: string;
  dueDate: string;
  statementBalance: number;
  minimumPayment: number | null;
  amountPaid: number;
  pending: number;
  status: StatementStatus;
  isDeclared: boolean;
  declaredSource: 'Imported' | 'Manual' | null;
  charges: number;
  credits: number;
  isCurrent: boolean;
}

export interface CreditCardActivity {
  label: string;
  purchases: number;
  refunds: number;
  payments: number;
  interest: number;
  fees: number;
  cashAdvances: number;
  adjustments: number;
}

export interface Installment {
  number: number;
  amount: number;
  closingDate: string;
  dueDate: string;
  status: 'Upcoming' | 'Billed';
}

export interface InstallmentPlan {
  id: string;
  transactionId: string;
  description: string;
  categoryName: string | null;
  purchaseDate: string;
  originalAmount: number;
  numberOfInstallments: number;
  installmentAmount: number;
  interestRate: number | null;
  billedInstallments: number;
  nextInstallmentNumber: number | null;
  nextInstallmentAmount: number | null;
  nextInstallmentClosingDate: string | null;
  nextInstallmentDueDate: string | null;
  outstandingAmount: number;
  status: 'Active' | 'Completed' | 'Cancelled';
  installments: Installment[];
}

export interface CreditCardDetail {
  card: CreditCardSummary;
  currentCycle: CreditCardCycle | null;
  lastStatement: CreditCardStatement | null;
  thisMonth: CreditCardActivity;
  activeInstallments: InstallmentPlan[];
  recentMovements: TransactionListItem[];
}

export interface CreateCreditCardRequest {
  name: string;
  providerCode: string;
  lastFour: string | null;
  creditLimit: number;
  closingDay: number;
  paymentDueDay: number;
  autoReserve: boolean;
  network?: CardNetwork | null;
  currency?: string | null;
  currentDebt?: number | null;
}

export interface UpdateCreditCardRequest {
  name?: string | null;
  lastFour?: string | null;
  creditLimit: number;
  closingDay: number;
  paymentDueDay: number;
  autoReserve: boolean;
  network?: CardNetwork | null;
}

export interface DeclareStatementRequest {
  closingDate: string;
  dueDate: string;
  statementBalance: number;
  minimumPayment?: number | null;
}

export interface CreateInstallmentPlanRequest {
  transactionId: string;
  numberOfInstallments: number;
  interestRate?: number | null;
}

export interface RegisterCardPaymentRequest {
  amount: number;
  sourceAccountId: string | null;
  paidAt?: string | null;
  clientRequestId?: string | null;
}

export interface CardPaymentResult {
  cardTransactionId: string;
  bankTransactionId: string | null;
  card: CreditCardSummary;
}

export interface CardPaymentSuggestion {
  bankTransactionId: string;
  bankAccountId: string;
  bankAccountAlias: string;
  date: string;
  amount: number;
  description: string;
  cardTransactionId: string | null;
  reason: 'matches_card_payment' | 'looks_like_card_payment';
}

/** Documentos legales (GET /legal/{terminos|privacidad}), markdown simple. */
export interface LegalDocument {
  kind: 'terminos' | 'privacidad';
  title: string;
  version: string;
  markdown: string;
  /** Faltan datos del responsable en la configuración del servidor. */
  isIncomplete: boolean;
}

/** Qué aceptó la persona y si debe aceptar una versión nueva (GET /legal/status). */
export interface LegalStatus {
  termsVersion: string;
  privacyVersion: string;
  acceptedTermsVersion: string | null;
  acceptedPrivacyVersion: string | null;
  needsAcceptance: boolean;
  /** null = nunca se le preguntó. */
  analyticsConsent: boolean | null;
  contactEmail: string;
}

/** Lo que la persona marca al registrarse (LOPDP art. 8): nunca marcado por defecto. */
export interface RegisterConsents {
  acceptedTerms: boolean;
  confirmedAdult: boolean;
  analyticsConsent: boolean;
}
